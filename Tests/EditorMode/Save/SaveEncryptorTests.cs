using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Save
{
    /// <summary>
    /// <see cref="SaveEncryptor"/>（AES 流加解密）行为与安全语义测试。
    /// </summary>
    public class SaveEncryptorTests
    {
        private SaveEncryptor _encryptor;

        [SetUp]
        public void SetUp() => _encryptor = new SaveEncryptor();

        [Test]
        public void DefaultKey_IsShippingPlaceholder()
        {
            // 锁定占位默认值：默认密钥必须是"未配置"信号而非可用凭证，
            // 防止项目侧忘记注入密钥时静默使用框架内置值发布。
            Assert.AreEqual("CHANGE_ME_BEFORE_SHIPPING", _encryptor.Key);
            Assert.AreEqual("CHANGE_ME_SALT", _encryptor.Salt);
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

            Assert.Greater(encrypted.Length, 0, "加密输出不应为空");
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
        public void WrongKey_ThrowsCryptographicException()
        {
            var payload = new byte[64];
            new Random(1).NextBytes(payload);

            var encrypted = new MemoryStream();
            using (var input = new MemoryStream(payload))
            {
                _encryptor.Encrypt(input, encrypted, "key-a");
            }

            encrypted.Position = 0;
            var decrypted = new MemoryStream();
            Assert.Throws<CryptographicException>(
                () => _encryptor.Decrypt(encrypted, decrypted, "key-b"),
                "错误密钥应抛出加密异常而非产出垃圾明文");
        }

        [Test]
        public void TamperedFinalBlock_ThrowsCryptographicException()
        {
            var payload = new byte[64];
            new Random(2).NextBytes(payload);

            var encrypted = new MemoryStream();
            using (var input = new MemoryStream(payload))
            {
                _encryptor.Encrypt(input, encrypted, "key-a");
            }

            var cipher = encrypted.ToArray();
            cipher[^1] ^= 0xFF; // 篡改最后一个密文块（PKCS7 填充校验必失败）

            var decrypted = new MemoryStream();
            using (var input = new MemoryStream(cipher))
            {
                Assert.Throws<CryptographicException>(
                    () => _encryptor.Decrypt(input, decrypted, "key-a"),
                    "末块篡改应被填充校验拦截");
            }
        }

        [Test]
        public void Salt_ChangesCiphertext()
        {
            var payload = Encoding.UTF8.GetBytes("same plaintext, same key, different salt");

            var withSaltA = Encrypt(payload, "key-a", _encryptor.Salt);
            _encryptor.Salt = "ANOTHER_SALT";
            var withSaltB = Encrypt(payload, "key-a", _encryptor.Salt);

            CollectionAssert.AreNotEqual(withSaltA, withSaltB, "盐文应参与密钥派生并改变密文");
            Assert.AreEqual(payload, Decrypt(withSaltB, "key-a", _encryptor.Salt), "配套盐文应可解密");
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

        private byte[] Decrypt(byte[] cipher, string key, string salt)
        {
            _encryptor.Salt = salt;
            var output = new MemoryStream();
            using (var input = new MemoryStream(cipher))
            {
                _encryptor.Decrypt(input, output, key);
            }

            return output.ToArray();
        }
    }
}
