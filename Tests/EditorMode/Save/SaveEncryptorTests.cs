using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Save
{
    /// <summary>
    /// <see cref="SaveEncryptor"/>（AES-CBC + 随机 IV + HMAC-SHA256 + PBKDF2-SHA256）行为与安全语义测试。
    /// </summary>
    public class SaveEncryptorTests
    {
        private SaveEncryptor _encryptor;

        [SetUp]
        public void SetUp()
        {
            _encryptor = new SaveEncryptor
            {
                // 测试专用低迭代次数加速派生；派生强度由 DefaultIterations 契约测试锁定
                Iterations = 1000
            };
        }

        [Test]
        public void DefaultKey_IsShippingPlaceholder()
        {
            // 锁定占位默认值：默认密钥必须是"未配置"信号而非可用凭证，
            // 防止项目侧忘记注入密钥时静默使用框架内置值发布。
            Assert.AreEqual("CHANGE_ME_BEFORE_SHIPPING", _encryptor.Key);
            Assert.AreEqual("CHANGE_ME_SALT", _encryptor.Salt);
        }

        [Test]
        public void DefaultIterations_IsDocumentedBaseline()
        {
            // 派生强度基线：默认迭代次数降低会被静默削弱安全预算，此处锁死
            Assert.AreEqual(100000, SaveEncryptor.DefaultIterations);
            Assert.AreEqual(SaveEncryptor.DefaultIterations, new SaveEncryptor().Iterations);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(1024)]
        [TestCase(65536)]
        public void RoundTrip_PreservesPayload(int size)
        {
            var payload = new byte[size];
            var random = new Random(20260906);
            random.NextBytes(payload);

            var encrypted = new MemoryStream();
            using (var input = new MemoryStream(payload))
            {
                _encryptor.Encrypt(input, encrypted, "key-a");
            }

            // 载荷布局 [16B IV][密文][32B MAC]——空明文也含填充块
            Assert.GreaterOrEqual(encrypted.Length, 16 + 16 + 32, "密文应至少包含 IV、一个 AES 块与 HMAC 摘要");
            if (size > 0)
            {
                CollectionAssert.AreNotEqual(payload, encrypted.ToArray(), "密文不应等于明文");
            }

            var decrypted = new MemoryStream();
            encrypted.Position = 0;
            _encryptor.Decrypt(encrypted, decrypted, "key-a");

            CollectionAssert.AreEqual(payload, decrypted.ToArray(), $"解密应还原 {size} 字节明文");
        }

        [Test]
        public void RoundTrip_TextPayload()
        {
            const string text = "Moirai Framework 加密往返测试 ⑵ 混合字符!@#";

            var encrypted = new MemoryStream();
            using (var input = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            {
                _encryptor.Encrypt(input, encrypted, "key-a");
            }

            var decrypted = new MemoryStream();
            encrypted.Position = 0;
            _encryptor.Decrypt(encrypted, decrypted, "key-a");

            Assert.AreEqual(text, Encoding.UTF8.GetString(decrypted.ToArray()));
        }

        [Test]
        public void SameInput_ProducesDifferentCiphertext()
        {
            // 随机 IV：相同明文与密钥的两次加密产出不同密文，杜绝静态 IV 的前缀模式泄露
            var payload = Encoding.UTF8.GetBytes("identical plaintext");

            var first = EncryptToBytes(payload, "key-a");
            var second = EncryptToBytes(payload, "key-a");

            CollectionAssert.AreNotEqual(first, second, "随机 IV 应使每次加密产出不同密文");

            var decrypted = new MemoryStream();
            using (var input = new MemoryStream(second))
            {
                _encryptor.Decrypt(input, decrypted, "key-a");
            }

            CollectionAssert.AreEqual(payload, decrypted.ToArray(), "不同密文仍应可解密回同一明文");
        }

        [Test]
        public void WrongKey_ThrowsCryptographicException()
        {
            var payload = new byte[64];
            new Random(1).NextBytes(payload);

            // 错误密钥在 HMAC 层即被拦截（先验 MAC 后解密），不得产出垃圾明文
            var cipher = EncryptToBytes(payload, "key-a");
            var decrypted = new MemoryStream();
            using (var input = new MemoryStream(cipher))
            {
                Assert.Throws<CryptographicException>(
                    () => _encryptor.Decrypt(input, decrypted, "key-b"),
                    "错误密钥应抛出加密异常而非产出垃圾明文");
            }
        }

        [Test]
        public void TamperedCiphertext_ThrowsCryptographicException()
        {
            // 位翻转篡改（含非末块位置）必须被 HMAC 拦截——CBC-only 旧格式对此不可检测
            var payload = new byte[64];
            new Random(2).NextBytes(payload);

            var cipher = EncryptToBytes(payload, "key-a");
            cipher[20] ^= 0xFF;

            var decrypted = new MemoryStream();
            using (var input = new MemoryStream(cipher))
            {
                Assert.Throws<CryptographicException>(
                    () => _encryptor.Decrypt(input, decrypted, "key-a"),
                    "密文中段篡改应被 HMAC 校验拦截");
            }
        }

        [Test]
        public void TamperedIv_ThrowsCryptographicException()
        {
            var payload = new byte[64];
            new Random(3).NextBytes(payload);

            var cipher = EncryptToBytes(payload, "key-a");
            cipher[0] ^= 0xFF;

            var decrypted = new MemoryStream();
            using (var input = new MemoryStream(cipher))
            {
                Assert.Throws<CryptographicException>(
                    () => _encryptor.Decrypt(input, decrypted, "key-a"),
                    "IV 篡改应被 HMAC 校验拦截");
            }
        }

        [Test]
        public void TruncatedCiphertext_ThrowsCryptographicException()
        {
            var payload = new byte[64];
            new Random(4).NextBytes(payload);

            var cipher = EncryptToBytes(payload, "key-a");
            var truncated = new byte[cipher.Length - 8];
            Array.Copy(cipher, truncated, truncated.Length);

            var decrypted = new MemoryStream();
            using (var input = new MemoryStream(truncated))
            {
                Assert.Throws<CryptographicException>(
                    () => _encryptor.Decrypt(input, decrypted, "key-a"),
                    "截断密文应被长度/HMAC 校验拦截");
            }
        }

        [Test]
        public void Salt_ChangesCiphertext_AndRoundTrips()
        {
            var payload = Encoding.UTF8.GetBytes("same plaintext, same key, different salt");

            var withSaltA = Encrypt(payload, "key-a", "SALT_A");
            _encryptor.Salt = "SALT_B";
            var withSaltB = Encrypt(payload, "key-a", "SALT_B");

            CollectionAssert.AreNotEqual(withSaltA, withSaltB, "盐文应参与密钥派生并改变密文");

            var decrypted = new MemoryStream();
            using (var input = new MemoryStream(withSaltB))
            {
                _encryptor.Decrypt(input, decrypted, "key-a");
            }

            CollectionAssert.AreEqual(payload, decrypted.ToArray(), "配套盐文应可解密");
        }

        [Test]
        public void NonAsciiSalt_RoundTrips()
        {
            // 盐文经 UTF-8 编码参与派生：非 ASCII 盐文必须无损（旧 ASCII 编码会静默失真）
            _encryptor.Salt = "盐文-Ｓａｌｔ-🧂";
            var payload = Encoding.UTF8.GetBytes("non-ascii salt roundtrip");

            var cipher = EncryptToBytes(payload, "key-a");
            var decrypted = new MemoryStream();
            using (var input = new MemoryStream(cipher))
            {
                _encryptor.Decrypt(input, decrypted, "key-a");
            }

            CollectionAssert.AreEqual(payload, decrypted.ToArray());
        }

        [Test]
        public void IterationsMismatch_ThrowsCryptographicException()
        {
            // 迭代次数参与密钥派生：不一致即派生不同密钥，在 HMAC 层被拦截
            var payload = new byte[32];
            new Random(5).NextBytes(payload);

            var cipher = EncryptToBytes(payload, "key-a");
            _encryptor.Iterations = 1001;

            var decrypted = new MemoryStream();
            using (var input = new MemoryStream(cipher))
            {
                Assert.Throws<CryptographicException>(
                    () => _encryptor.Decrypt(input, decrypted, "key-a"),
                    "迭代次数不一致应导致派生密钥不同并被 HMAC 拦截");
            }
        }

        private byte[] Encrypt(byte[] payload, string key, string salt)
        {
            _encryptor.Salt = salt;
            var output = new MemoryStream();
            using (var input = new MemoryStream(payload))
            {
                _encryptor.Encrypt(input, output, key);
            }

            return output.ToArray();
        }

        private byte[] EncryptToBytes(byte[] payload, string key)
        {
            return Encrypt(payload, key, _encryptor.Salt);
        }
    }
}
