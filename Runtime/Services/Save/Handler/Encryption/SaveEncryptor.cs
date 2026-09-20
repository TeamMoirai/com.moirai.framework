using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档加密器：AES-256-CBC + 随机 IV + HMAC-SHA256（encrypt-then-MAC）+ PBKDF2（SHA-256）密钥派生。
    /// <para>密文布局：<c>[16B 随机 IV][密文][32B HMAC-SHA256(IV‖密文)]</c>；
    /// 加密密钥与 MAC 密钥由同一次 PBKDF2 派生的 64 字节拆分（前 32B 加密、后 32B 认证）。</para>
    /// <para>防篡改依赖 HMAC（先验证 MAC 后解密，常数时间比较）；防意外存储损坏由文件头 CRC32 承担。</para>
    /// </summary>
    public class SaveEncryptor
    {
        /// <summary>
        /// 默认 PBKDF2 迭代次数（移动端预算内兼顾派生强度的基线值，可经 <c>SaveServiceSettings</c> 覆盖）。
        /// </summary>
        public const int DEFAULT_ITERATIONS = 100000;

        /// <summary>默认占位口令（标记「未配置」；上线前必须替换为项目专属密钥）。</summary>
        internal const string DEFAULT_PASSPHRASE = "CHANGE_ME_BEFORE_SHIPPING";

        /// <summary>默认占位盐文（标记「未配置」；上线前必须替换为项目专属盐文）。</summary>
        internal const string DEFAULT_SALT = "CHANGE_ME_SALT";

        /// <summary>IV 字节数（AES 块大小）。</summary>
        private const int IV_SIZE = 16;

        /// <summary>AES-256 密钥字节数。</summary>
        internal const int ENCRYPTION_KEY_SIZE = 32;

        /// <summary>HMAC-SHA256 密钥/摘要字节数。</summary>
        internal const int MAC_SIZE = 32;

        /// <summary>AES-CBC 最小密文长度（空明文的 PKCS7 填充块）。</summary>
        private const int MIN_CIPHER_SIZE = 16;

        /// <summary>
        /// 保存和加载文件的密钥。
        /// <para>SECURITY: 上线前必须替换为项目专属密钥（默认占位值用于标记「未配置」）。</para>
        /// </summary>
        public virtual string Key { get; set; } = DEFAULT_PASSPHRASE;

        /// <summary>
        /// 加密盐文（UTF-8 编码后参与 PBKDF2 密钥派生）。
        /// <para>SECURITY: 上线前必须替换为项目专属盐文。</para>
        /// </summary>
        public virtual string Salt { get; set; } = DEFAULT_SALT;

        /// <summary>
        /// PBKDF2 迭代次数（由 <c>SaveServiceSettings</c> 注入覆盖）。
        /// </summary>
        public virtual int Iterations { get; set; } = DEFAULT_ITERATIONS;

        /// <summary>派生密钥材料快照（口令/盐/迭代次数 + 派生结果的不可变整体，原子读避免字段组撕裂）。</summary>
        private sealed class DerivedKeySnapshot
        {
            internal readonly string Key;
            internal readonly string Salt;
            internal readonly int Iterations;
            internal readonly byte[] DerivedKeys;

            internal DerivedKeySnapshot(string key, string salt, int iterations, byte[] derivedKeys)
            {
                Key = key;
                Salt = salt;
                Iterations = iterations;
                DerivedKeys = derivedKeys;
            }

            /// <summary>
            /// 判断快照是否匹配当前派生参数。
            /// </summary>
            internal bool Matches(string key, string salt, int iterations)
            {
                return Iterations == iterations
                    && string.Equals(Key, key, StringComparison.Ordinal)
                    && string.Equals(Salt, salt, StringComparison.Ordinal);
            }
        }

        /// <summary>派生密钥缓存（同参数重复加解密时跳过 PBKDF2 重派生——每次派生为 10 万次迭代级开销；整体替换原子读）。</summary>
        [NonSerialized] private volatile DerivedKeySnapshot _derivedKeyCache;

        /// <summary>缓存安装锁（仅保护「查缓存 → 安装」竞态；PBKDF2 派生本体在锁外执行，不阻塞并发存档 IO 线程）。</summary>
        [NonSerialized] private readonly object _deriveLock = new object();

        #region 公共流式 API [PUBLIC STREAM API]

        /// <summary>
        /// 使用参数中传入的密钥将指定的输入流加密到指定的输出流中。
        /// </summary>
        /// <param name="inputStream">明文输入流（从当前位置读取）。</param>
        /// <param name="outputStream">密文输出流。</param>
        /// <param name="sKey">加密密钥。</param>
        public virtual void Encrypt(Stream inputStream, Stream outputStream, string sKey)
        {
            byte[] plaintext = ReadAllBytes(inputStream);
            SaveError error = TryEncrypt(plaintext, sKey, out byte[] encrypted);
            if (error != SaveError.None)
            {
                throw new CryptographicException(StringUtility.Format("Save encryption failed with error '{0}'.", error));
            }

            outputStream.Write(encrypted, 0, encrypted.Length);
        }

        /// <summary>
        /// 使用参数中传入的密钥将输入流解密为输出流。
        /// </summary>
        /// <param name="inputStream">密文输入流（从当前位置读取）。</param>
        /// <param name="outputStream">明文输出流。</param>
        /// <param name="sKey">解密密钥。</param>
        public virtual void Decrypt(Stream inputStream, Stream outputStream, string sKey)
        {
            byte[] encrypted = ReadAllBytes(inputStream);
            SaveError error = TryDecrypt(encrypted, sKey, out byte[] plaintext);
            if (error != SaveError.None)
            {
                throw new CryptographicException(StringUtility.Format("Save decryption failed with error '{0}'.", error));
            }

            outputStream.Write(plaintext, 0, plaintext.Length);
        }

        #endregion

        #region 内部字节通路 [INTERNAL BYTE PIPELINE]

        /// <summary>
        /// 加密字节载荷（随机 IV + AES-CBC + HMAC），供加密处理器管线调用。
        /// </summary>
        /// <param name="plaintext">明文字节。</param>
        /// <param name="sKey">加密密钥。</param>
        /// <param name="encrypted">成功时的密文字节。</param>
        /// <returns>错误码。</returns>
        internal SaveError TryEncrypt(byte[] plaintext, string sKey, out byte[] encrypted)
        {
            encrypted = null;
            if (plaintext == null)
            {
                return SaveError.InvalidArgument;
            }

            byte[] derivedKeys = DeriveKeys(sKey);
            return TryEncryptWithMaterial(plaintext, derivedKeys.AsSpan(0, ENCRYPTION_KEY_SIZE).ToArray(), derivedKeys.AsSpan(ENCRYPTION_KEY_SIZE, MAC_SIZE).ToArray(), out encrypted);
        }

        /// <summary>
        /// 加密字节载荷（密钥材料直给，跳过 PBKDF2 派生——供 <see cref="ISaveKeyProvider"/> 管线调用）。
        /// </summary>
        /// <param name="plaintext">明文字节。</param>
        /// <param name="encryptionKey">加密密钥（<see cref="ENCRYPTION_KEY_SIZE"/> 字节）。</param>
        /// <param name="macKey">MAC 密钥（<see cref="MAC_SIZE"/> 字节）。</param>
        /// <param name="encrypted">成功时的密文字节。</param>
        /// <returns>错误码。</returns>
        internal SaveError TryEncryptWithMaterial(byte[] plaintext, byte[] encryptionKey, byte[] macKey, out byte[] encrypted)
        {
            return TryEncryptWithMaterial(plaintext, 0, plaintext?.Length ?? 0, encryptionKey, macKey, out encrypted);
        }

        /// <summary>
        /// 加密缓冲区有效区间内的明文（密钥材料直给——区间形式供池化缓冲区直通，避免精确数组二次拷贝）。
        /// </summary>
        /// <param name="plaintext">明文缓冲区。</param>
        /// <param name="offset">有效区间起始偏移。</param>
        /// <param name="length">有效区间字节数。</param>
        /// <param name="encryptionKey">加密密钥（<see cref="ENCRYPTION_KEY_SIZE"/> 字节）。</param>
        /// <param name="macKey">MAC 密钥（<see cref="MAC_SIZE"/> 字节）。</param>
        /// <param name="encrypted">成功时的密文字节。</param>
        /// <returns>错误码。</returns>
        internal SaveError TryEncryptWithMaterial(byte[] plaintext, int offset, int length, byte[] encryptionKey, byte[] macKey, out byte[] encrypted)
        {
            encrypted = null;
            if (plaintext == null || offset < 0 || length < 0 || plaintext.Length - offset < length)
            {
                return SaveError.InvalidArgument;
            }

            if (!IsValidKeyMaterial(encryptionKey, macKey))
            {
                return SaveError.InvalidArgument;
            }

            // 随机 IV：相同明文/密钥每次加密产出不同密文，杜绝静态 IV 的前缀模式泄露
            byte[] iv = new byte[IV_SIZE];
            RandomNumberGenerator.Fill(iv);

            byte[] ciphertext;
            using (Aes algorithm = Aes.Create())
            {
                algorithm.Key = encryptionKey;
                algorithm.IV = iv;
                using (ICryptoTransform encryptor = algorithm.CreateEncryptor())
                {
                    ciphertext = encryptor.TransformFinalBlock(plaintext, offset, length);
                }
            }

            encrypted = new byte[IV_SIZE + ciphertext.Length + MAC_SIZE];
            Buffer.BlockCopy(iv, 0, encrypted, 0, IV_SIZE);
            Buffer.BlockCopy(ciphertext, 0, encrypted, IV_SIZE, ciphertext.Length);

            byte[] mac = ComputeMac(macKey, encrypted, 0, IV_SIZE + ciphertext.Length);
            Buffer.BlockCopy(mac, 0, encrypted, IV_SIZE + ciphertext.Length, MAC_SIZE);
            return SaveError.None;
        }

        /// <summary>
        /// 解密字节载荷（先验证 HMAC 后解密），供加密处理器管线调用。
        /// </summary>
        /// <param name="encrypted">密文字节。</param>
        /// <param name="sKey">解密密钥。</param>
        /// <param name="plaintext">成功时的明文字节。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>、<see cref="SaveError.InvalidArgument"/>、
        /// <see cref="SaveError.InvalidFormat"/>、<see cref="SaveError.IntegrityCheckFailed"/> 或 <see cref="SaveError.DecryptionFailed"/>。</returns>
        internal SaveError TryDecrypt(byte[] encrypted, string sKey, out byte[] plaintext)
        {
            plaintext = null;
            if (encrypted == null)
            {
                return SaveError.InvalidArgument;
            }

            if (encrypted.Length < IV_SIZE + MIN_CIPHER_SIZE + MAC_SIZE)
            {
                return SaveError.InvalidFormat;
            }

            byte[] derivedKeys = DeriveKeys(sKey);
            return TryDecryptWithMaterial(encrypted, derivedKeys.AsSpan(0, ENCRYPTION_KEY_SIZE).ToArray(), derivedKeys.AsSpan(ENCRYPTION_KEY_SIZE, MAC_SIZE).ToArray(), out plaintext);
        }

        /// <summary>
        /// 解密字节载荷（密钥材料直给，先验证 HMAC 后解密——供 <see cref="ISaveKeyProvider"/> 管线调用）。
        /// </summary>
        /// <param name="encrypted">密文字节。</param>
        /// <param name="encryptionKey">加密密钥（<see cref="ENCRYPTION_KEY_SIZE"/> 字节）。</param>
        /// <param name="macKey">MAC 密钥（<see cref="MAC_SIZE"/> 字节）。</param>
        /// <param name="plaintext">成功时的明文字节。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>、<see cref="SaveError.InvalidArgument"/>、
        /// <see cref="SaveError.InvalidFormat"/>、<see cref="SaveError.IntegrityCheckFailed"/> 或 <see cref="SaveError.DecryptionFailed"/>。</returns>
        internal SaveError TryDecryptWithMaterial(byte[] encrypted, byte[] encryptionKey, byte[] macKey, out byte[] plaintext)
        {
            return TryDecryptWithMaterial(encrypted, 0, encrypted?.Length ?? 0, encryptionKey, macKey, out plaintext);
        }

        /// <summary>
        /// 解密缓冲区有效区间内的密文（密钥材料直给，先验证 HMAC 后解密——区间形式供文件字节直通，避免载荷二次拷贝）。
        /// </summary>
        /// <param name="encrypted">密文缓冲区。</param>
        /// <param name="offset">有效区间起始偏移。</param>
        /// <param name="length">有效区间字节数。</param>
        /// <param name="encryptionKey">加密密钥（<see cref="ENCRYPTION_KEY_SIZE"/> 字节）。</param>
        /// <param name="macKey">MAC 密钥（<see cref="MAC_SIZE"/> 字节）。</param>
        /// <param name="plaintext">成功时的明文字节。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>、<see cref="SaveError.InvalidArgument"/>、
        /// <see cref="SaveError.InvalidFormat"/>、<see cref="SaveError.IntegrityCheckFailed"/> 或 <see cref="SaveError.DecryptionFailed"/>。</returns>
        internal SaveError TryDecryptWithMaterial(byte[] encrypted, int offset, int length, byte[] encryptionKey, byte[] macKey, out byte[] plaintext)
        {
            plaintext = null;
            if (encrypted == null || offset < 0 || length < 0 || encrypted.Length - offset < length)
            {
                return SaveError.InvalidArgument;
            }

            if (length < IV_SIZE + MIN_CIPHER_SIZE + MAC_SIZE)
            {
                return SaveError.InvalidFormat;
            }

            if (!IsValidKeyMaterial(encryptionKey, macKey))
            {
                return SaveError.InvalidArgument;
            }

            int ciphertextLength = length - IV_SIZE - MAC_SIZE;

            // encrypt-then-MAC：先对 [IV‖密文] 验证 HMAC（常数时间比较），未过验不触碰解密器
            byte[] expectedMac = ComputeMac(macKey, encrypted, offset, IV_SIZE + ciphertextLength);
            if (!CryptographicOperations.FixedTimeEquals(expectedMac, encrypted.AsSpan(offset + length - MAC_SIZE, MAC_SIZE)))
            {
                return SaveError.IntegrityCheckFailed;
            }

            try
            {
                using (Aes algorithm = Aes.Create())
                {
                    algorithm.Key = encryptionKey;
                    algorithm.IV = encrypted.AsSpan(offset, IV_SIZE).ToArray();
                    using (ICryptoTransform decryptor = algorithm.CreateDecryptor())
                    {
                        plaintext = decryptor.TransformFinalBlock(encrypted, offset + IV_SIZE, ciphertextLength);
                    }
                }

                return SaveError.None;
            }
            catch (CryptographicException)
            {
                return SaveError.DecryptionFailed;
            }
        }

        #endregion

        #region 流式写链 [STREAMING WRITE PIPELINE]

        /// <summary>
        /// 打开加密写流（密钥材料直给）：明文经返回流写入即 AES-256-CBC 加密并落底层流，关闭返回流收尾
        /// （FlushFinalBlock 补齐末块密文并追加 HMAC-SHA256 摘要尾）。
        /// <para>输出布局与 <see cref="TryEncryptWithMaterial(byte[], byte[], byte[], out byte[])"/> 完全一致：
        /// <c>[16B 随机 IV][密文][32B HMAC(IV‖密文)]</c>——流式写全程无整档明文/密文驻留（流式容器管线核心）。</para>
        /// </summary>
        /// <param name="target">密文落点流（生命周期由调用方管理；关闭返回流不关闭该流）。</param>
        /// <param name="encryptionKey">加密密钥（<see cref="ENCRYPTION_KEY_SIZE"/> 字节）。</param>
        /// <param name="macKey">MAC 密钥（<see cref="MAC_SIZE"/> 字节）。</param>
        /// <returns>加密包装流（只写；调用方负责关闭以收尾密文与 MAC 尾）。</returns>
        internal Stream OpenEncryptStreamWithMaterial(Stream target, byte[] encryptionKey, byte[] macKey)
        {
            if (target == null || !IsValidKeyMaterial(encryptionKey, macKey))
            {
                throw new ArgumentException("Encrypt stream requires a writable target and valid key material.");
            }

            // 随机 IV：相同明文/密钥每次加密产出不同密文，杜绝静态 IV 的前缀模式泄露
            byte[] iv = new byte[IV_SIZE];
            RandomNumberGenerator.Fill(iv);
            target.Write(iv, 0, IV_SIZE);

            var algorithm = Aes.Create();
            algorithm.Key = encryptionKey;
            algorithm.IV = iv;
            ICryptoTransform encryptor = algorithm.CreateEncryptor();

            // 链：CryptoStream(密文) → HmacWriteStream(喂 MAC + 关闭时追加摘要尾) → target；IV 先行喂入 HMAC
            var hmacStream = new HmacWriteStream(target, macKey);
            hmacStream.Feed(iv, 0, IV_SIZE);
            var cryptoStream = new CryptoStream(hmacStream, encryptor, CryptoStreamMode.Write);
            return new EncryptWriteStream(cryptoStream, hmacStream, encryptor, algorithm);
        }

        /// <summary>
        /// HMAC-SHA256 增量写包装流：写入透传到底层流并增量喂入 HMAC；关闭时定稿摘要并追加到底层流末尾。
        /// </summary>
        private sealed class HmacWriteStream : Stream
        {
            /// <summary>底层目标流。</summary>
            private readonly Stream _inner;

            /// <summary>HMAC 机件。</summary>
            private readonly HMACSHA256 _hmac;

            /// <summary>是否已关闭（幂等守卫）。</summary>
            private bool _disposed;

            /// <summary>
            /// 创建 HMAC 写包装流。
            /// </summary>
            /// <param name="inner">底层目标流。</param>
            /// <param name="macKey">MAC 密钥。</param>
            internal HmacWriteStream(Stream inner, byte[] macKey)
            {
                _inner = inner;
                _hmac = new HMACSHA256(macKey);
            }

            /// <summary>
            /// 手动喂入前缀数据（IV 等已写入底层流但需参与 HMAC 的前缀字节）。
            /// </summary>
            /// <param name="buffer">数据缓冲区。</param>
            /// <param name="offset">起始偏移。</param>
            /// <param name="count">字节数。</param>
            internal void Feed(byte[] buffer, int offset, int count)
            {
                _hmac.TransformBlock(buffer, offset, count, null, 0);
            }

            /// <inheritdoc />
            public override bool CanRead => false;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => true;

            /// <inheritdoc />
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                _hmac.TransformBlock(buffer, offset, count, null, 0);
                _inner.Write(buffer, offset, count);
            }

            /// <inheritdoc />
            public override void Flush()
            {
                _inner.Flush();
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            /// <inheritdoc />
            public override void SetLength(long value) => throw new NotSupportedException();

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                {
                    _disposed = true;
                    _hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    byte[] mac = _hmac.Hash;
                    _inner.Write(mac, 0, mac.Length);
                    _hmac.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// 加密写流组合体：写入面为 <see cref="CryptoStream"/>；关闭时按序收尾（密文补齐 → MAC 尾 → 机件释放）。
        /// </summary>
        private sealed class EncryptWriteStream : Stream
        {
            /// <summary>AES-CBC 加密流（写入面）。</summary>
            private readonly CryptoStream _cryptoStream;

            /// <summary>HMAC 中间层（密文喂入 + 关闭时追加摘要尾）。</summary>
            private readonly HmacWriteStream _hmacStream;

            /// <summary>加密变换器。</summary>
            private readonly ICryptoTransform _encryptor;

            /// <summary>AES 算法机件。</summary>
            private readonly Aes _algorithm;

            /// <summary>是否已关闭（幂等守卫）。</summary>
            private bool _disposed;

            /// <summary>
            /// 创建加密写流组合体。
            /// </summary>
            /// <param name="cryptoStream">加密流（写入面）。</param>
            /// <param name="hmacStream">HMAC 中间层。</param>
            /// <param name="encryptor">加密变换器。</param>
            /// <param name="algorithm">AES 算法机件。</param>
            internal EncryptWriteStream(CryptoStream cryptoStream, HmacWriteStream hmacStream, ICryptoTransform encryptor, Aes algorithm)
            {
                _cryptoStream = cryptoStream;
                _hmacStream = hmacStream;
                _encryptor = encryptor;
                _algorithm = algorithm;
            }

            /// <inheritdoc />
            public override bool CanRead => false;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => true;

            /// <inheritdoc />
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                _cryptoStream.Write(buffer, offset, count);
            }

            /// <inheritdoc />
            public override void Flush()
            {
                _cryptoStream.Flush();
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            /// <inheritdoc />
            public override void SetLength(long value) => throw new NotSupportedException();

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                {
                    _disposed = true;
                    _cryptoStream.FlushFinalBlock();
                    _cryptoStream.Dispose();
                    _hmacStream.Dispose();
                    _encryptor.Dispose();
                    _algorithm.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        #endregion

        #region 流式读链 [STREAMING READ PIPELINE]

        /// <summary>
        /// 解密读链失败分型异常（携带 <see cref="SaveError"/>——读管线归一为对应错误码）。
        /// </summary>
        internal sealed class SaveDecryptStreamException : Exception
        {
            /// <summary>错误码。</summary>
            internal readonly SaveError Error;

            /// <summary>
            /// 创建解密读链异常。
            /// </summary>
            /// <param name="error">错误码。</param>
            /// <param name="message">异常消息。</param>
            internal SaveDecryptStreamException(SaveError error, string message) : base(message)
            {
                Error = error;
            }
        }

        /// <summary>
        /// 打开解密读流（密钥材料直给）：源流读取 <c>[16B IV][密文][32B HMAC]</c> 布局的存储载荷——
        /// <para>两遍流式（encrypt-then-MAC 语义完整）：第一遍流式 HMAC 预验（64KB 池化循环喂入 [IV‖密文]，比对尾部摘要，
        /// 不符抛 <see cref="SaveDecryptStreamException"/>（<see cref="SaveError.IntegrityCheckFailed"/>）——先验证后解密，杜绝填充 oracle；
        /// 预验通过后冻结载荷 CRC 包装层并 rewind 回载荷起点，第二遍限长 [IV‖密文] 解密链（HMAC 尾留在限长段外——任意时刻关闭均安全）。</para>
        /// </summary>
        /// <param name="source">存储载荷源流（<see cref="Crc32.Crc32ReadStream"/> 包装层——第一遍预验读取经此累计载荷 CRC；底层流须可寻址）。</param>
        /// <param name="payloadLength">存储载荷总字节数（文件头口径——含 IV/密文/HMAC 尾）。</param>
        /// <param name="encryptionKey">加密密钥（<see cref="ENCRYPTION_KEY_SIZE"/> 字节）。</param>
        /// <param name="macKey">MAC 密钥（<see cref="MAC_SIZE"/> 字节）。</param>
        /// <returns>解密读流（只读；调用方负责关闭）。</returns>
        internal Stream OpenDecryptStreamWithMaterial(Stream source, long payloadLength, byte[] encryptionKey, byte[] macKey)
        {
            if (source == null || !IsValidKeyMaterial(encryptionKey, macKey))
            {
                throw new ArgumentException("Decrypt stream requires a readable source and valid key material.");
            }

            if (payloadLength < IV_SIZE + MIN_CIPHER_SIZE + MAC_SIZE)
            {
                throw new SaveDecryptStreamException(SaveError.InvalidFormat, "Encrypted payload shorter than the minimum layout.");
            }

            var crcStream = source as Crc32.Crc32ReadStream;
            if (crcStream == null)
            {
                throw new ArgumentException("Decrypt stream requires the payload CRC wrapping stream (Crc32ReadStream).", nameof(source));
            }

            long payloadStart = crcStream.Inner.Position;

            // 第一遍：流式 HMAC 预验（读取同时经 CRC 包装层累计校验值）——未过验不触碰解密器
            VerifyMacStreaming(source, payloadLength, macKey);

            // rewind：冻结 CRC（第二遍解密读不再重复喂入）并回到载荷起点
            crcStream.Freeze();
            crcStream.Seek(payloadStart, SeekOrigin.Begin);

            // 第二遍：限长 [IV‖密文] 解密链（HMAC 尾留在限长段外，永不进入解密器）
            var bounded = new BoundedStream(source, payloadLength - MAC_SIZE);
            byte[] iv = ReadExactly(bounded, IV_SIZE);

            var algorithm = Aes.Create();
            algorithm.Key = encryptionKey;
            algorithm.IV = iv;
            ICryptoTransform decryptor = algorithm.CreateDecryptor();
            var cryptoStream = new CryptoStream(bounded, decryptor, CryptoStreamMode.Read);
            return new DecryptReadStream(cryptoStream, decryptor, algorithm);
        }

        /// <summary>
        /// 流式 HMAC 预验：顺序读取 [IV‖密文] 增量喂入 HMAC，与尾部 32B 摘要常数时间比对。
        /// </summary>
        /// <param name="source">存储载荷源流（当前位置即载荷起点；读取经其 CRC 包装层累计校验值）。</param>
        /// <param name="payloadLength">存储载荷总字节数（含 HMAC 尾）。</param>
        /// <param name="macKey">MAC 密钥。</param>
        private static void VerifyMacStreaming(Stream source, long payloadLength, byte[] macKey)
        {
            const int BufferSize = 64 * 1024;
            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                using (var hmac = new HMACSHA256(macKey))
                {
                    long remaining = payloadLength - MAC_SIZE;
                    while (remaining > 0)
                    {
                        int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (read == 0)
                        {
                            throw new SaveDecryptStreamException(SaveError.InvalidFormat, "Unexpected end of encrypted payload.");
                        }

                        hmac.TransformBlock(buffer, 0, read, null, 0);
                        remaining -= read;
                    }

                    byte[] expectedMac = ReadExactly(source, MAC_SIZE);
                    hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    if (!CryptographicOperations.FixedTimeEquals(hmac.Hash, expectedMac))
                    {
                        throw new SaveDecryptStreamException(SaveError.IntegrityCheckFailed, "Save payload HMAC mismatch (tampered or wrong key).");
                    }
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// 自源流精确读取指定字节数（读不足判别为格式损坏）。
        /// </summary>
        /// <param name="source">源流。</param>
        /// <param name="count">字节数。</param>
        /// <returns>读到的字节。</returns>
        private static byte[] ReadExactly(Stream source, int count)
        {
            var buffer = new byte[count];
            int total = 0;
            while (total < count)
            {
                int read = source.Read(buffer, total, count - total);
                if (read == 0)
                {
                    throw new SaveDecryptStreamException(SaveError.InvalidFormat, "Unexpected end of encrypted payload.");
                }

                total += read;
            }

            return buffer;
        }

        /// <summary>
        /// 限长读包装流：仅暴露源流前 N 字节（尾部字节留在底层流中供后续顺序读取——HMAC 尾剥离的核心机件）。
        /// </summary>
        private sealed class BoundedStream : Stream
        {
            /// <summary>底层源流。</summary>
            private readonly Stream _inner;

            /// <summary>剩余可暴露字节数。</summary>
            private long _remaining;

            /// <summary>
            /// 创建限长读包装流。
            /// </summary>
            /// <param name="inner">底层源流。</param>
            /// <param name="maxBytes">暴露的最大字节数。</param>
            internal BoundedStream(Stream inner, long maxBytes)
            {
                _inner = inner;
                _remaining = maxBytes;
            }

            /// <summary>底层源流（读尽段外尾部用）。</summary>
            internal Stream Inner => _inner;

            /// <inheritdoc />
            public override bool CanRead => true;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => false;

            /// <inheritdoc />
            public override long Length => _remaining;

            /// <inheritdoc />
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_remaining <= 0)
                {
                    return 0;
                }

                int read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
                _remaining -= read;
                return read;
            }

            /// <inheritdoc />
            public override void Flush()
            {
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            /// <inheritdoc />
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        /// <summary>
        /// 解密读流组合体：读取面为 <see cref="CryptoStream"/>（HMAC 已预验——限长源流读尽即密文恰好耗尽）。
        /// <para>关闭语义：Mono 读模式 <see cref="CryptoStream"/> 的 Dispose 会对剩余数据 FinalDecrypt——
        /// 限长段内剩余恒为合法密文（HMAC 尾在段外），但提前关闭时末块不完整会抛填充异常，此处吞并（提前关闭语义，数据未消费不完整非错误）。</para>
        /// </summary>
        private sealed class DecryptReadStream : Stream
        {
            /// <summary>AES-CBC 解密流（读取面）。</summary>
            private readonly CryptoStream _cryptoStream;

            /// <summary>解密变换器。</summary>
            private readonly ICryptoTransform _decryptor;

            /// <summary>AES 算法机件。</summary>
            private readonly Aes _algorithm;

            /// <summary>是否已关闭（幂等守卫）。</summary>
            private bool _disposed;

            /// <summary>
            /// 创建解密读流组合体。
            /// </summary>
            /// <param name="cryptoStream">解密流（读取面）。</param>
            /// <param name="decryptor">解密变换器。</param>
            /// <param name="algorithm">AES 算法机件。</param>
            internal DecryptReadStream(CryptoStream cryptoStream, ICryptoTransform decryptor, Aes algorithm)
            {
                _cryptoStream = cryptoStream;
                _decryptor = decryptor;
                _algorithm = algorithm;
            }

            /// <inheritdoc />
            public override bool CanRead => true;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => false;

            /// <inheritdoc />
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                return _cryptoStream.Read(buffer, offset, count);
            }

            /// <inheritdoc />
            public override void Flush()
            {
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            /// <inheritdoc />
            public override void SetLength(long value) => throw new NotSupportedException();

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                {
                    _disposed = true;
                    try
                    {
                        _cryptoStream.Dispose();
                    }
                    catch (CryptographicException)
                    {
                        // 提前关闭（未读尽）时 Mono 读模式 CryptoStream 对不完整末块 FinalDecrypt 抛填充异常——提前关闭语义，吞并
                    }

                    _decryptor.Dispose();
                    _algorithm.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 计算缓冲区有效区间（IV‖密文）的 HMAC-SHA256。
        /// </summary>
        /// <param name="macKey">MAC 密钥。</param>
        /// <param name="buffer">承载 [IV‖密文] 的缓冲区。</param>
        /// <param name="offset">有效区间起始偏移。</param>
        /// <param name="length">参与计算的区间长度。</param>
        /// <returns>HMAC 摘要。</returns>
        private static byte[] ComputeMac(byte[] macKey, byte[] buffer, int offset, int length)
        {
            using (HMACSHA256 hmac = new HMACSHA256(macKey))
            {
                return hmac.ComputeHash(buffer, offset, length);
            }
        }

        /// <summary>
        /// 校验密钥材料长度（加密密钥 32 字节、MAC 密钥 32 字节）。
        /// </summary>
        /// <param name="encryptionKey">加密密钥。</param>
        /// <param name="macKey">MAC 密钥。</param>
        /// <returns>长度合法返回 <c>true</c>。</returns>
        private static bool IsValidKeyMaterial(byte[] encryptionKey, byte[] macKey)
        {
            return encryptionKey != null && encryptionKey.Length == ENCRYPTION_KEY_SIZE
                && macKey != null && macKey.Length == MAC_SIZE;
        }

        /// <summary>
        /// PBKDF2-SHA256 派生 64 字节密钥材料（前 32B 加密密钥、后 32B MAC 密钥）——静态纯函数，供密钥提供方在工作线程调用。
        /// </summary>
        /// <param name="passphrase">口令。</param>
        /// <param name="salt">盐文。</param>
        /// <param name="iterations">迭代次数。</param>
        /// <returns>64 字节密钥材料。</returns>
        internal static byte[] DeriveKeyMaterial(string passphrase, string salt, int iterations)
        {
            using (Rfc2898DeriveBytes algorithm = new Rfc2898DeriveBytes(passphrase, Encoding.UTF8.GetBytes(salt), iterations, HashAlgorithmName.SHA256))
            {
                return algorithm.GetBytes(ENCRYPTION_KEY_SIZE + MAC_SIZE);
            }
        }

        /// <summary>
        /// PBKDF2-SHA256 派生 64 字节密钥材料（前 32B 加密密钥、后 32B MAC 密钥）。
        /// <para>同（口令, 盐文, 迭代次数）组合命中实例缓存时无锁复用；未命中时派生本体在锁外执行（10 万迭代级开销不阻塞并发线程），
        /// 安装阶段才取锁二次判读——并发同参派生结果幂等，后到者覆盖安装等值结果。</para>
        /// </summary>
        /// <param name="sKey">口令。</param>
        /// <returns>密钥材料。</returns>
        private byte[] DeriveKeys(string sKey)
        {
            DerivedKeySnapshot snapshot = _derivedKeyCache;
            if (snapshot != null && snapshot.Matches(sKey, Salt, Iterations))
            {
                return snapshot.DerivedKeys;
            }

            // 锁外派生：并发同参各自派生等值结果，安装时先到先得
            byte[] derivedKeys = DeriveKeyMaterial(sKey, Salt, Iterations);

            lock (_deriveLock)
            {
                snapshot = _derivedKeyCache;
                if (snapshot != null && snapshot.Matches(sKey, Salt, Iterations))
                {
                    return snapshot.DerivedKeys;
                }

                _derivedKeyCache = new DerivedKeySnapshot(sKey, Salt, Iterations, derivedKeys);
                return derivedKeys;
            }
        }

        /// <summary>
        /// 将流自当前位置起整体读为字节数组。
        /// </summary>
        /// <param name="stream">输入流。</param>
        /// <returns>字节内容。</returns>
        private static byte[] ReadAllBytes(Stream stream)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        #endregion
    }
}
