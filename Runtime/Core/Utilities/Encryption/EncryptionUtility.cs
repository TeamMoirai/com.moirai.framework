using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Moirai.Atropos
{
    /// <summary>
    /// 加密解密相关的实用函数。
    /// </summary>
    public static class EncryptionUtility
    {
        internal const int QUICK_ENCRYPT_LENGTH = 220;

        /// <summary>
        /// 将 bytes 使用 code 做异或运算的快速版本。
        /// </summary>
        /// <param name="bytes">原始二进制流。</param>
        /// <param name="code">异或二进制流。</param>
        /// <returns>异或后的二进制流。</returns>
        public static byte[] GetQuickXorBytes(byte[] bytes, byte[] code)
        {
            return GetXorBytes(bytes, 0, QUICK_ENCRYPT_LENGTH, code);
        }

        /// <summary>
        /// 将 bytes 使用 code 做异或运算的快速版本。此方法将复用并改写传入的 bytes 作为返回值，而不额外分配内存空间。
        /// </summary>
        /// <param name="bytes">原始及异或后的二进制流。</param>
        /// <param name="code">异或二进制流。</param>
        public static void GetQuickSelfXorBytes(byte[] bytes, byte[] code)
        {
            GetSelfXorBytes(bytes, 0, QUICK_ENCRYPT_LENGTH, code);
        }

        /// <summary>
        /// 将 bytes 使用 code 做异或运算。
        /// </summary>
        /// <param name="bytes">原始二进制流。</param>
        /// <param name="code">异或二进制流。</param>
        /// <returns>异或后的二进制流。</returns>
        public static byte[] GetXorBytes(byte[] bytes, byte[] code)
        {
            if (bytes == null)
            {
                return null;
            }

            return GetXorBytes(bytes, 0, bytes.Length, code);
        }

        /// <summary>
        /// 将 bytes 使用 code 做异或运算。此方法将复用并改写传入的 bytes 作为返回值，而不额外分配内存空间。
        /// </summary>
        /// <param name="bytes">原始及异或后的二进制流。</param>
        /// <param name="code">异或二进制流。</param>
        public static void GetSelfXorBytes(byte[] bytes, byte[] code)
        {
            if (bytes == null)
            {
                return;
            }

            GetSelfXorBytes(bytes, 0, bytes.Length, code);
        }

        /// <summary>
        /// 将 bytes 使用 code 做异或运算。
        /// </summary>
        /// <param name="bytes">原始二进制流。</param>
        /// <param name="startIndex">异或计算的开始位置。</param>
        /// <param name="length">异或计算长度，若小于 0，则计算整个二进制流。</param>
        /// <param name="code">异或二进制流。</param>
        /// <returns>异或后的二进制流。</returns>
        public static byte[] GetXorBytes(byte[] bytes, int startIndex, int length, byte[] code)
        {
            if (bytes == null)
            {
                return null;
            }

            int bytesLength = bytes.Length;
            byte[] results = new byte[bytesLength];
            Array.Copy(bytes, 0, results, 0, bytesLength);
            GetSelfXorBytes(results, startIndex, length, code);
            return results;
        }

        /// <summary>
        /// 将 bytes 使用 code 做异或运算。此方法将复用并改写传入的 bytes 作为返回值，而不额外分配内存空间。
        /// </summary>
        /// <param name="bytes">原始及异或后的二进制流。</param>
        /// <param name="startIndex">异或计算的开始位置。</param>
        /// <param name="length">异或计算长度。</param>
        /// <param name="code">异或二进制流。</param>
        public static void GetSelfXorBytes(byte[] bytes, int startIndex, int length, byte[] code)
        {
            if (bytes == null)
            {
                return;
            }

            if (code == null)
            {
                throw new GameException("Code is invalid.");
            }

            int codeLength = code.Length;
            if (codeLength <= 0)
            {
                throw new GameException("Code length is invalid.");
            }

            if (startIndex < 0 || length < 0 || startIndex + length > bytes.Length)
            {
                throw new GameException("Start index or length is invalid.");
            }

            int codeIndex = startIndex % codeLength;
            for (int i = startIndex; i < startIndex + length; i++)
            {
                bytes[i] ^= code[codeIndex++];
                codeIndex %= codeLength;
            }
        }

        /// <summary>
        /// 生成一个新 <see cref="Guid"/> 并按指定格式说明符格式化为字符串。
        /// </summary>
        /// <param name="format">格式说明符：
   		/// <para>"N" 32 位，例如 "33ee30121c43457eabb7e838a5e052e6"</para>
        /// <para>"D" 32 位, 由连字符分隔，例如 "33ee3012-1c43-457e-abb7-e838a5e052e6"</para>
        /// <para>"B" 32 位，用连字符分隔，用大括号括起来，例如 "{33ee3012-1c43-457e-abb7-e838a5e052e6}"</para>
        /// <para>"P" 32 位，用连字符分隔，括在括号中，例如 "(33ee3012-1c43-457e-abb7-e838a5e052e6)"</para>
        /// <para>"X" 32 位，四个十六进制值括在大括号中，其中第四个值是八个十六进制值的子集，这些值也括在大括号中， <br />
        /// 例如 "{0x33ee3012,0x1c43,0x457e,{0xab,0xb7,0xe8,0x38,0xa5,0xe0,0x52,0xe6}}"</para>
        /// </param>
        /// <returns>格式化后的 GUID 字符串。</returns>
        public static string GenerateGuid(string format)
        {
            return Guid.NewGuid().ToString(format);
        }
        
        static StringBuilder stringBuilderCache = new StringBuilder(1024);
        
        #region MD5 [MD5]
        
        /// <summary>
        /// 生成 MD5 摘要。
        /// </summary>
        /// <param name="context">字节数组。</param>
        /// <returns>哈希值。</returns>
        public static string GenerateMD5(byte[] context)
        {
#if NET_STANDARD_2_0
            using (var hash = MD5.Create())
#elif NET_4_6
            using (var hash = MD5Cng.Create())
#endif
            {
                byte[] data = hash.ComputeHash(context);
                var sBuilder = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }

                return sBuilder.ToString();
            }
        }
        
        /// <summary>
        /// MD5加密，返回16位加密后的大写16进制字符。
        /// </summary>
        /// <param name="context">需要加密的字符。</param>
        /// <returns>加密后的结果。</returns>
        /// <remarks>安全提示：MD5 已在密码学上被攻破，安全敏感场景请改用 SHA-256 或更强的哈希算法。</remarks>
        [System.Obsolete("MD5 is cryptographically broken. Use SHA-256 (e.g. HmacSHA256) for security-sensitive hashing.")]
        public static string MD5Encrypt16(string context)
        {
            byte[] md5Bytes = Encoding.UTF8.GetBytes(context);
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] cryptString = md5.ComputeHash(md5Bytes);
            stringBuilderCache.Clear();
            for (int i = 4; i < 12; i++)
            {
                stringBuilderCache.Append(cryptString[i].ToString("X2"));
            }

            return stringBuilderCache.ToString();
        }

        /// <summary>
        /// MD5加密，返回32位加密后的大写16进制字符。
        /// </summary>
        /// <param name="context">需要加密的字符。</param>
        /// <returns>加密后的结果。</returns>
        /// <remarks>安全提示：MD5 已在密码学上被攻破，安全敏感场景请改用 SHA-256 或更强的哈希算法。</remarks>
        [System.Obsolete("MD5 is cryptographically broken. Use SHA-256 (e.g. HmacSHA256) for security-sensitive hashing.")]
        public static string MD5Encrypt32(string context)
        {
            byte[] md5Bytes = Encoding.UTF8.GetBytes(context);
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] cryptString = md5.ComputeHash(md5Bytes);
            stringBuilderCache.Clear();
            int length = cryptString.Length;
            for (int i = 0; i < length; i++)
            {
                //X大写的16进制，x小写
                stringBuilderCache.Append(cryptString[i].ToString("X2"));
            }

            return stringBuilderCache.ToString();
        }

        /// <summary>
        //// MD5加密。
        /// </summary>
        /// <param name="context">需要加密的字符。</param>
        /// <returns>加密后的结果。</returns>
        /// <remarks>安全提示：MD5 已在密码学上被攻破，安全敏感场景请改用 SHA-256 或更强的哈希算法。</remarks>
        [System.Obsolete("MD5 is cryptographically broken. Use SHA-256 (e.g. HmacSHA256) for security-sensitive hashing.")]
        public static string MD5Encrypt(string context)
        {
            byte[] md5Bytes = Encoding.UTF8.GetBytes(context);
            MD5 md5 = MD5.Create();
            byte[] cryptBytes = md5.ComputeHash(md5Bytes);
            int length = cryptBytes.Length;
            stringBuilderCache.Clear();
            for (int i = 0; i < length; i++)
            {
                //X大写的16进制，x小写
                stringBuilderCache.Append(cryptBytes[i].ToString("X2"));
            }

            return stringBuilderCache.ToString();
        }

        #endregion
        
        #region AES密钥 [AES KEY]
        
        /// <summary>
        /// 将字符串转为 8 字节密钥：不足右补零、超出截断，空串返回空数组。
        /// </summary>
        /// <remarks>本工具类的 AES 加密要求密钥为 16、24 或 32 字节，8 字节密钥不能用于 AES。</remarks>
        /// <param name="key">原始密钥信息。</param>
        /// <returns>8 字节密钥。</returns>
        public static byte[] Generate8BytesAESKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return Array.Empty<byte>();
            var dstLen = 8;
            var srcBytes = Encoding.UTF8.GetBytes(key);
            byte[] dstBytes = new byte[dstLen];
            var srcLen = srcBytes.Length;
            if (srcLen > dstLen)
            {
                Array.Copy(srcBytes, 0, dstBytes, 0, dstLen);
            }
            else
            {
                var diffLen = dstLen - srcLen;
                var diffBytes = new byte[diffLen];
                Array.Copy(srcBytes, 0, dstBytes, 0, srcLen);
                Array.Copy(diffBytes, 0, dstBytes, srcLen, diffLen);
            }

            return dstBytes;
        }

        /// <summary>
        /// 生成16位密钥。
        /// </summary>
        /// <param name="key">原始密钥信息。</param>
        /// <returns>加密后的值。</returns>
        public static byte[] Generate16BytesAESKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return Array.Empty<byte>();
            var dstLen = 16;
            var srcBytes = Encoding.UTF8.GetBytes(key);
            byte[] dstBytes = new byte[dstLen];
            var srcLen = srcBytes.Length;
            if (srcLen > dstLen)
            {
                Array.Copy(srcBytes, 0, dstBytes, 0, dstLen);
            }
            else
            {
                var diffLen = dstLen - srcLen;
                var diffBytes = new byte[diffLen];
                Array.Copy(srcBytes, 0, dstBytes, 0, srcLen);
                Array.Copy(diffBytes, 0, dstBytes, srcLen, diffLen);
            }

            return dstBytes;
        }

        /// <summary>
        /// 生成24位密钥。
        /// </summary>
        /// <param name="key">原始密钥信息。</param>
        /// <returns>加密后的值。</returns>
        public static byte[] Generate24BytesAESKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return Array.Empty<byte>();
            var dstLen = 24;
            var srcBytes = Encoding.UTF8.GetBytes(key);
            byte[] dstBytes = new byte[dstLen];
            var srcLen = srcBytes.Length;
            if (srcLen > dstLen)
            {
                Array.Copy(srcBytes, 0, dstBytes, 0, dstLen);
            }
            else
            {
                var diffLen = dstLen - srcLen;
                var diffBytes = new byte[diffLen];
                Array.Copy(srcBytes, 0, dstBytes, 0, srcLen);
                Array.Copy(diffBytes, 0, dstBytes, srcLen, diffLen);
            }

            return dstBytes;
        }

        /// <summary>
        /// 生成32位密钥。
        /// </summary>
        /// <param name="key">原始密钥信息。</param>
        /// <returns>加密后的值。</returns>
        public static byte[] Generate32BytesAESKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return Array.Empty<byte>();
            var dstLen = 32;
            var srcBytes = Encoding.UTF8.GetBytes(key);
            byte[] dstBytes = new byte[dstLen];
            var srcLen = srcBytes.Length;
            if (srcLen > dstLen)
            {
                Array.Copy(srcBytes, 0, dstBytes, 0, dstLen);
            }
            else
            {
                var diffLen = dstLen - srcLen;
                var diffBytes = new byte[diffLen];
                Array.Copy(srcBytes, 0, dstBytes, 0, srcLen);
                Array.Copy(diffBytes, 0, dstBytes, srcLen, diffLen);
            }

            return dstBytes;
        }
        
        #endregion

        #region HMAC-SHA [HMACSHA]

        /// <summary>
        /// 加密算法HMACSHA1 base64。
        /// </summary>
        /// <param name="context">被加密的数据。</param>
        /// <param name="key">加密密码。</param>
        /// <returns>加密后的字段。</returns>
        public static string HmacSHA1ToBase64(string context, string key)
        {
            string encrpytedResult = string.Empty;
            using (HMACSHA1 mac = new HMACSHA1(Encoding.UTF8.GetBytes(key)))
            {
                byte[] hashMsg = mac.ComputeHash(Encoding.UTF8.GetBytes(context));
                encrpytedResult = Convert.ToBase64String(hashMsg);
            }

            return encrpytedResult;
        }

        /// <summary>
        /// 加密算法HMACSHA1。
        /// </summary>
        /// <param name="context">被加密的数据。</param>
        /// <param name="key">加密密码。</param>
        /// <returns>加密后的字段。</returns>
        public static string HmacSHA1(string context, string key)
        {
            using (HMACSHA1 mac = new HMACSHA1(Encoding.UTF8.GetBytes(key)))
            {
                byte[] hash = mac.ComputeHash(Encoding.UTF8.GetBytes(context));
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        /// <summary>
        /// 加密算法HMACSHA1，输出16位字符串。
        /// </summary>
        /// <param name="context">被加密的数据。</param>
        /// <param name="key">加密密码。</param>
        /// <returns>加密后的字段。</returns>
        public static string HmacSHA1ToHex(string context, string key)
        {
            string encrpytedResult = string.Empty;
            using (HMACSHA1 mac = new HMACSHA1(Encoding.UTF8.GetBytes(key)))
            {
                byte[] hashBytes = mac.ComputeHash(Encoding.UTF8.GetBytes(context));
                int length = hashBytes.Length;
                stringBuilderCache.Clear();
                for (int i = 0; i < length; i++)
                {
                    stringBuilderCache.Append(hashBytes[i].ToString("X2"));
                }

                encrpytedResult = stringBuilderCache.ToString();
            }

            return encrpytedResult;
        }

        /// <summary>
        /// 加密算法HMACSHA256。
        /// </summary>
        /// <param name="context">被加密的数据。</param>
        /// <param name="key">加密密钥。</param>
        /// <returns>加密后的字段。</returns>
        public static string HmacSHA256(string context, string key)
        {
            using (HMACSHA256 mac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                byte[] hash = mac.ComputeHash(Encoding.UTF8.GetBytes(context));
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        /// <summary>
        /// 加密算法HMACSHA256 base64。
        /// </summary>
        /// <param name="context">被加密的数据。</param>
        /// <param name="key">加密密钥。</param>
        /// <returns>加密后的字段。</returns>
        public static string HmacSHA256ToBase64(string context, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var textBytes = Encoding.UTF8.GetBytes(context);
            using (HMACSHA256 mac = new HMACSHA256(keyBytes))
            {
                byte[] hash = mac.ComputeHash(textBytes);
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// 加密算法HMACSHA256，输出16位字符串。
        /// </summary>
        /// <param name="context">被加密的数据。</param>
        /// <param name="key">加密密码。</param>
        /// <returns>加密后的字段。</returns>
        public static string HmacSHA256ToHex(string context, string key)
        {
            string encrpytedResult = string.Empty;
            using (HMACSHA256 mac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                byte[] hashBytes = mac.ComputeHash(Encoding.UTF8.GetBytes(context));
                int length = hashBytes.Length;
                stringBuilderCache.Clear();
                for (int i = 0; i < length; i++)
                {
                    stringBuilderCache.Append(hashBytes[i].ToString("X2"));
                }

                encrpytedResult = stringBuilderCache.ToString();
            }

            return encrpytedResult;
        }
        
        #endregion

        #region AES [AES]

        /// <summary>
        /// AES 对称加密字节数组，返回 Base64 字符串（前置 16 字节随机 IV）。
        /// </summary>
        /// <remarks>密钥长度必须为 16、24 或 32 字节；CBC 模式、PKCS7 填充。</remarks>
        /// <param name="context">待加密的字节数组。</param>
        /// <param name="key">对称密钥。</param>
        /// <returns>加密后的 Base64 字符串。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> 或 <paramref name="key"/> 为 null。</exception>
        public static string AESEncryptByteToString(byte[] context, byte[] key)
        {
            if (context == null)
                throw new ArgumentNullException("context is invalid !");
            if (key == null)
                throw new ArgumentNullException("key is invalid !");
            using (var aes = new AesCryptoServiceProvider()
                       { Key = key, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
            {
                aes.GenerateIV();
                var iv = aes.IV;
                using (MemoryStream ms = new MemoryStream())
                {
                    ms.Write(iv, 0, iv.Length);
                    using (CryptoStream cryptoStream = new CryptoStream(ms, aes.CreateEncryptor(aes.Key, iv),
                               CryptoStreamMode.Write))
                    using (var writer = new BinaryWriter(cryptoStream))
                    {
                        writer.Write(context);
                        cryptoStream.FlushFinalBlock();
                    }

                    var buf = ms.ToArray();
                    return Convert.ToBase64String(buf, 0, buf.Length);
                }
            }
        }

        /// <summary>
        /// AES 对称解密字节数组（前 16 字节为 IV），返回明文的 Base64 字符串。
        /// </summary>
        /// <remarks>密钥长度必须为 16、24 或 32 字节；CBC 模式、PKCS7 填充，与 <see cref="AESEncryptByteToString"/> 配对。</remarks>
        /// <param name="context">待解密的字节数组（含 IV 前缀）。</param>
        /// <param name="key">对称密钥。</param>
        /// <returns>明文的 Base64 字符串。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> 或 <paramref name="key"/> 为 null。</exception>
        public static string AESDecryptByteToString(byte[] context, byte[] key)
        {
            if (context == null)
                throw new ArgumentNullException("context is invalid !");
            if (key == null)
                throw new ArgumentNullException("key is invalid !");
            using (var aes = new AesCryptoServiceProvider()
                       { Key = key, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
            {
                var iv = new byte[16];
                Array.Copy(context, 0, iv, 0, iv.Length);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cryptoStream = new CryptoStream(ms, aes.CreateDecryptor(aes.Key, iv),
                               CryptoStreamMode.Write))
                    {
                        using (BinaryWriter writer = new BinaryWriter(cryptoStream))
                        {
                            writer.Write(context, iv.Length, context.Length - iv.Length);
                        }
                    }

                    var buf = ms.ToArray();
                    return Convert.ToBase64String(buf, 0, buf.Length);
                }
            }
        }

        /// <summary>
        /// AES 对称加密字节数组，返回密文字节数组（前置 16 字节随机 IV）。
        /// </summary>
        /// <remarks>密钥长度必须为 16、24 或 32 字节；CBC 模式、PKCS7 填充。</remarks>
        /// <param name="context">待加密的字节数组。</param>
        /// <param name="key">对称密钥。</param>
        /// <returns>加密后的字节数组。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> 或 <paramref name="key"/> 为 null。</exception>
        public static byte[] AESEncryptByteToByte(byte[] context, byte[] key)
        {
            if (context == null)
                throw new ArgumentNullException("context is invalid !");
            if (key == null)
                throw new ArgumentNullException("key is invalid !");
            using (var aes = new AesCryptoServiceProvider()
                       { Key = key, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
            {
                aes.GenerateIV();
                var iv = aes.IV;
                using (MemoryStream ms = new MemoryStream())
                {
                    ms.Write(iv, 0, iv.Length);
                    using (CryptoStream cryptoStream = new CryptoStream(ms, aes.CreateEncryptor(aes.Key, iv),
                               CryptoStreamMode.Write))
                    using (var writer = new BinaryWriter(cryptoStream))
                    {
                        writer.Write(context);
                        cryptoStream.FlushFinalBlock();
                    }

                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// AES 对称解密字节数组（前 16 字节为 IV），返回明文字节数组。
        /// </summary>
        /// <remarks>密钥长度必须为 16、24 或 32 字节；CBC 模式、PKCS7 填充，与 <see cref="AESEncryptByteToByte"/> 配对。</remarks>
        /// <param name="context">待解密的字节数组（含 IV 前缀）。</param>
        /// <param name="key">对称密钥。</param>
        /// <returns>解密后的字节数组。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> 或 <paramref name="key"/> 为 null。</exception>
        public static byte[] AESDecryptByteToByte(byte[] context, byte[] key)
        {
            if (context == null)
                throw new ArgumentNullException("context is invalid !");
            if (key == null)
                throw new ArgumentNullException("key is invalid !");
            using (var aes = new AesCryptoServiceProvider()
                       { Key = key, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
            {
                var iv = new byte[16];
                Array.Copy(context, 0, iv, 0, iv.Length);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cryptoStream = new CryptoStream(ms, aes.CreateDecryptor(aes.Key, iv),
                               CryptoStreamMode.Write))
                    {
                        using (BinaryWriter writer = new BinaryWriter(cryptoStream))
                        {
                            writer.Write(context, iv.Length, context.Length - iv.Length);
                        }
                    }

                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// AES 对称加密字符串，返回 Base64 字符串（前置 16 字节 IV）。
        /// </summary>
        /// <remarks>密钥长度必须为 16、24 或 32 字节；CBC 模式、PKCS7 填充。</remarks>
        /// <param name="context">待加密的字符串。</param>
        /// <param name="key">对称密钥。</param>
        /// <returns>加密后的 Base64 字符串。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> 为空或 <paramref name="key"/> 为 null。</exception>
        public static string AESEncryptStringToString(string context, byte[] key)
        {
            if (string.IsNullOrEmpty(context))
                throw new ArgumentNullException("context is invalid ! ");
            if (key == null)
                throw new ArgumentNullException("key is invalid ! ");
            using (var aes = new AesCryptoServiceProvider())
            {
                var iv = aes.IV;
                using (MemoryStream ms = new MemoryStream())
                {
                    ms.Write(iv, 0, iv.Length);
                    using (var cryptStream =
                           new CryptoStream(ms, aes.CreateEncryptor(key, aes.IV), CryptoStreamMode.Write))
                    {
                        using (StreamWriter writer = new StreamWriter(cryptStream))
                        {
                            writer.Write(context);
                        }
                    }

                    var buf = ms.ToArray();
                    return Convert.ToBase64String(buf, 0, buf.Length);
                }
            }
        }

        /// <summary>
        /// AES 对称解密字符串，输入为 Base64 字符串（前 16 字节 IV），返回明文。
        /// </summary>
        /// <remarks>密钥长度必须为 16、24 或 32 字节；CBC 模式、PKCS7 填充，与 <see cref="AESEncryptStringToString"/> 配对。</remarks>
        /// <param name="context">待解密的 Base64 字符串。</param>
        /// <param name="key">对称密钥。</param>
        /// <returns>解密后的字符串。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> 为空或 <paramref name="key"/> 为 null。</exception>
        /// <exception cref="FormatException"><paramref name="context"/> 不是合法的 Base64。</exception>
        public static string AESDecryptStringToString(string context, byte[] key)
        {
            if (string.IsNullOrEmpty(context))
                throw new ArgumentNullException("context is invalid ! ");
            if (key == null)
                throw new ArgumentNullException("key is invalid ! ");
            var bytes = Convert.FromBase64String(context);
            using (var aes = new AesCryptoServiceProvider())
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    var iv = new byte[16];
                    ms.Read(iv, 0, 16);
                    using (var cryptStream =
                           new CryptoStream(ms, aes.CreateDecryptor(key, iv), CryptoStreamMode.Read))
                    {
                        using (StreamReader reader = new StreamReader(cryptStream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// AES 对称加密字符串，返回密文字节数组（前置 16 字节 IV）。
        /// </summary>
        /// <remarks>密钥长度必须为 16、24 或 32 字节；CBC 模式、PKCS7 填充。</remarks>
        /// <param name="context">待加密的字符串。</param>
        /// <param name="key">对称密钥。</param>
        /// <returns>加密后的字节数组。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> 为空或 <paramref name="key"/> 为 null。</exception>
        public static byte[] AESEncryptStringToByte(string context, byte[] key)
        {
            if (string.IsNullOrEmpty(context))
                throw new ArgumentNullException("context is invalid ! ");
            if (key == null)
                throw new ArgumentNullException("key is invalid ! ");
            using (var aes = new AesCryptoServiceProvider())
            {
                var iv = aes.IV;
                using (MemoryStream ms = new MemoryStream())
                {
                    ms.Write(iv, 0, iv.Length);
                    using (var cryptStream =
                           new CryptoStream(ms, aes.CreateEncryptor(key, aes.IV), CryptoStreamMode.Write))
                    {
                        using (StreamWriter writer = new StreamWriter(cryptStream))
                        {
                            writer.Write(context);
                        }
                    }

                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// AES 对称解密字符串（前 16 字节 IV，明文本身为 Base64），返回明文字节数组。
        /// </summary>
        /// <remarks>密钥长度必须为 16、24 或 32 字节；CBC 模式、PKCS7 填充，与 <see cref="AESEncryptStringToByte"/> 配对。</remarks>
        /// <param name="context">待解密的 Base64 字符串。</param>
        /// <param name="key">对称密钥。</param>
        /// <returns>解密后的字节数组。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> 为空或 <paramref name="key"/> 为 null。</exception>
        public static byte[] AESDecryptStringToByte(string context, byte[] key)
        {
            if (string.IsNullOrEmpty(context))
                throw new ArgumentNullException("context is invalid ! ");
            if (key == null)
                throw new ArgumentNullException("key is invalid ! ");
            var bytes = Convert.FromBase64String(context);
            using (var aes = new AesCryptoServiceProvider())
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    var iv = new byte[16];
                    ms.Read(iv, 0, 16);
                    using (var cryptStream =
                           new CryptoStream(ms, aes.CreateDecryptor(key, iv), CryptoStreamMode.Read))
                    {
                        using (StreamReader reader = new StreamReader(cryptStream))
                        {
                            var data = reader.ReadToEnd();
                            return Convert.FromBase64String(data);
                        }
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// 生成验证码。
        /// </summary>
        /// <param name="length">指定验证码的长度。</param>
        /// <returns>验证码字符串。</returns>
        public static string CreateValidateCode(int length)
        {
            string ch = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ1234567890@#$%&?";
            byte[] bytes = new byte[4];
            using (var cpt = new RNGCryptoServiceProvider())
            {
                cpt.GetBytes(bytes);
                var r = new Random(BitConverter.ToInt32(bytes, 0));
                stringBuilderCache.Clear();
                for (int i = 0; i < length; i++)
                {
                    stringBuilderCache.Append(ch[r.Next(ch.Length)]);
                }

                return stringBuilderCache.ToString();
            }
        }

        /// <summary>
        /// 异或加密（相同为0，不同为1）。
        /// </summary>
        /// <param name="context">需要加密的内容。</param>
        /// <param name="key">密钥。</param>
        /// <returns>加密后的内容。</returns>
        /// <code>
        /// X | Y | Result
        /// ==============
        /// 0 | 0 | 0
        /// 1 | 0 | 1
        /// 0 | 1 | 1
        /// 1 | 1 | 0
        /// </code>
        public static byte[] XorEncrypt(byte[] context, byte[] key)
        {
            byte[] outputBytes = new byte[context.Length];
            var cntLength = outputBytes.Length;
            var keyLength = key.Length;
            for (int i = 0; i < cntLength; i++)
            {
                outputBytes[i] = (byte)(context[i] ^ key[i % keyLength]);
            }

            return outputBytes;
        }

        /// <summary>
        /// 异或解密。
        /// </summary>
        /// <param name="context">需要解密的内容。</param>
        /// <param name="key">密钥。</param>
        /// <returns>解密后的内容。</returns>
        public static byte[] XorDecrypt(byte[] context, byte[] key)
        {
            byte[] outputBytes = new byte[context.Length];
            var cntLength = outputBytes.Length;
            var keyLength = key.Length;
            for (int i = 0; i < cntLength; i++)
            {
                outputBytes[i] = (byte)(context[i] ^ key[i % keyLength]);
            }

            return outputBytes;
        }
    }
}
