using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Moirai.Atropos;

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
        public const int DefaultIterations = 100000;

        /// <summary>IV 字节数（AES 块大小）。</summary>
        private const int IvSize = 16;

        /// <summary>AES-256 密钥字节数。</summary>
        private const int EncryptionKeySize = 32;

        /// <summary>HMAC-SHA256 密钥/摘要字节数。</summary>
        private const int MacSize = 32;

        /// <summary>AES-CBC 最小密文长度（空明文的 PKCS7 填充块）。</summary>
        private const int MinCipherSize = 16;

        /// <summary>
        /// 保存和加载文件的密钥。
        /// <para>SECURITY: 上线前必须替换为项目专属密钥（默认占位值用于标记「未配置」）。</para>
        /// </summary>
        public virtual string Key { get; set; } = "CHANGE_ME_BEFORE_SHIPPING";

        /// <summary>
        /// 加密盐文（UTF-8 编码后参与 PBKDF2 密钥派生）。
        /// <para>SECURITY: 上线前必须替换为项目专属盐文。</para>
        /// </summary>
        public virtual string Salt { get; set; } = "CHANGE_ME_SALT";

        /// <summary>
        /// PBKDF2 迭代次数（由 <c>SaveServiceSettings</c> 注入覆盖）。
        /// </summary>
        public virtual int Iterations { get; set; } = DefaultIterations;

        /// <summary>派生密钥缓存（同参数重复加解密时跳过 PBKDF2 重派生——每次派生为 10 万次迭代级开销）。</summary>
        [NonSerialized] private byte[] _cachedDerivedKeys;

        /// <summary>派生密钥缓存对应的口令。</summary>
        [NonSerialized] private string _cachedKey;

        /// <summary>派生密钥缓存对应的盐文。</summary>
        [NonSerialized] private string _cachedSalt;

        /// <summary>派生密钥缓存对应的迭代次数。</summary>
        [NonSerialized] private int _cachedIterations;

        /// <summary>派生密钥缓存访问锁（并发加解密不同文件时保护缓存字段；锁开销相对 PBKDF2 派生可忽略）。</summary>
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

            // 随机 IV：相同明文/密钥每次加密产出不同密文，杜绝静态 IV 的前缀模式泄露
            byte[] iv = new byte[IvSize];
            RandomNumberGenerator.Fill(iv);

            byte[] ciphertext;
            using (Aes algorithm = Aes.Create())
            {
                algorithm.Key = derivedKeys.AsSpan(0, EncryptionKeySize).ToArray();
                algorithm.IV = iv;
                using (ICryptoTransform encryptor = algorithm.CreateEncryptor())
                {
                    ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }
            }

            encrypted = new byte[IvSize + ciphertext.Length + MacSize];
            Buffer.BlockCopy(iv, 0, encrypted, 0, IvSize);
            Buffer.BlockCopy(ciphertext, 0, encrypted, IvSize, ciphertext.Length);

            byte[] mac = ComputeMac(derivedKeys, encrypted, IvSize + ciphertext.Length);
            Buffer.BlockCopy(mac, 0, encrypted, IvSize + ciphertext.Length, MacSize);
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

            if (encrypted.Length < IvSize + MinCipherSize + MacSize)
            {
                return SaveError.InvalidFormat;
            }

            byte[] derivedKeys = DeriveKeys(sKey);
            int ciphertextLength = encrypted.Length - IvSize - MacSize;

            // encrypt-then-MAC：先对 [IV‖密文] 验证 HMAC（常数时间比较），未过验不触碰解密器
            byte[] expectedMac = ComputeMac(derivedKeys, encrypted, IvSize + ciphertextLength);
            if (!CryptographicOperations.FixedTimeEquals(expectedMac, encrypted.AsSpan(encrypted.Length - MacSize, MacSize)))
            {
                return SaveError.IntegrityCheckFailed;
            }

            try
            {
                using (Aes algorithm = Aes.Create())
                {
                    algorithm.Key = derivedKeys.AsSpan(0, EncryptionKeySize).ToArray();
                    algorithm.IV = encrypted.AsSpan(0, IvSize).ToArray();
                    using (ICryptoTransform decryptor = algorithm.CreateDecryptor())
                    {
                        plaintext = decryptor.TransformFinalBlock(encrypted, IvSize, ciphertextLength);
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

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 计算载荷前缀（IV‖密文）的 HMAC-SHA256。
        /// </summary>
        /// <param name="derivedKeys">PBKDF2 派生的 64 字节密钥材料。</param>
        /// <param name="buffer">承载 [IV‖密文] 的缓冲区。</param>
        /// <param name="length">参与计算的前缀长度。</param>
        /// <returns>HMAC 摘要。</returns>
        private static byte[] ComputeMac(byte[] derivedKeys, byte[] buffer, int length)
        {
            using (HMACSHA256 hmac = new HMACSHA256(derivedKeys.AsSpan(EncryptionKeySize, MacSize).ToArray()))
            {
                return hmac.ComputeHash(buffer, 0, length);
            }
        }

        /// <summary>
        /// PBKDF2-SHA256 派生 64 字节密钥材料（前 32B 加密密钥、后 32B MAC 密钥）。
        /// <para>同（口令, 盐文, 迭代次数）组合命中实例缓存时直接复用；缓存判读与重派生在锁内串行——
        /// 并发派生同一参数结果幂等，锁仅消除缓存字段读写竞争。</para>
        /// </summary>
        /// <param name="sKey">口令。</param>
        /// <returns>密钥材料。</returns>
        private byte[] DeriveKeys(string sKey)
        {
            lock (_deriveLock)
            {
                if (_cachedDerivedKeys != null
                    && _cachedIterations == Iterations
                    && string.Equals(_cachedKey, sKey, StringComparison.Ordinal)
                    && string.Equals(_cachedSalt, Salt, StringComparison.Ordinal))
                {
                    return _cachedDerivedKeys;
                }

                using (Rfc2898DeriveBytes algorithm = new Rfc2898DeriveBytes(sKey, Encoding.UTF8.GetBytes(Salt), Iterations, HashAlgorithmName.SHA256))
                {
                    _cachedDerivedKeys = algorithm.GetBytes(EncryptionKeySize + MacSize);
                }

                _cachedKey = sKey;
                _cachedSalt = Salt;
                _cachedIterations = Iterations;
                return _cachedDerivedKeys;
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
