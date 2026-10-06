using System;
using System.Security.Cryptography;
using System.Text;
using Moirai.Atropos;
using NUnit.Framework;

namespace Utility
{
    /// <summary>
    /// 验证 <see cref="EncryptionUtility"/> 的 AES 往返与防篡改、XOR 自逆、密钥整备与 HMAC 确定性（存档与云存档链路的数据完整性契约）。
    /// </summary>
    /// <remarks>MD5 族已 <c>[Obsolete]</c>（密码学上被攻破），不锁用例；AES 族要求密钥 16/24/32 字节（CBC + PKCS7 + 前置随机 IV）。</remarks>
    public class EncryptionUtilityTests
    {
        private static byte[] Key(int length, byte seed)
        {
            byte[] key = new byte[length];
            for (int i = 0; i < length; i++)
            {
                key[i] = (byte)(seed + i);
            }

            return key;
        }

        [Test]
        public void AES_StringRoundtrip_With16And32ByteKeys_RestoresOriginal()
        {
            const string plaintext = "存档明文-Hello-Save-42";
            byte[] key16 = Key(16, 1);
            byte[] key32 = Key(32, 2);

            string cipher16 = EncryptionUtility.AESEncryptStringToString(plaintext, key16);
            string cipher32 = EncryptionUtility.AESEncryptStringToString(plaintext, key32);

            Assert.IsNotEmpty(cipher16);
            Assert.IsNotEmpty(cipher32);
            Assert.AreNotEqual(cipher16, cipher32, "同明文不同密钥的密文必须不同");
            Assert.AreEqual(plaintext, EncryptionUtility.AESDecryptStringToString(cipher16, key16), "16 字节密钥往返必须还原明文");
            Assert.AreEqual(plaintext, EncryptionUtility.AESDecryptStringToString(cipher32, key32), "32 字节密钥往返必须还原明文");
        }

        [Test]
        public void AES_ByteRoundtrip_ReturnsOriginalBytes()
        {
            byte[] payload = Encoding.UTF8.GetBytes("byte-payload-0123456789");
            byte[] key = Key(24, 7);

            byte[] cipher = EncryptionUtility.AESEncryptByteToByte(payload, key);
            byte[] restored = EncryptionUtility.AESDecryptByteToByte(cipher, key);

            Assert.AreEqual(payload, restored, "字节数组往返必须逐字节还原");
        }

        [Test]
        public void AES_TamperedCiphertext_ThrowsOnDecrypt()
        {
            byte[] payload = Encoding.UTF8.GetBytes("integrity-check");
            byte[] key = Key(16, 3);
            byte[] cipher = EncryptionUtility.AESEncryptByteToByte(payload, key);

            // IV 之后翻一个密文位：CBC + PKCS7 下必须在解密侧响亮失败，而不是静默给出错乱的明文
            cipher[cipher.Length - 1] ^= 0x01;
            Assert.Throws<CryptographicException>(() => EncryptionUtility.AESDecryptByteToByte(cipher, key),
                "被篡改的密文不得被当作有效载荷解出");
        }

        [Test]
        public void AES_NullArguments_ThrowArgumentNullException()
        {
            byte[] key = Key(16, 4);
            byte[] payload = { 1, 2, 3 };

            Assert.Throws<ArgumentNullException>(() => EncryptionUtility.AESEncryptByteToByte(null, key));
            Assert.Throws<ArgumentNullException>(() => EncryptionUtility.AESEncryptByteToByte(payload, null));
            Assert.Throws<ArgumentNullException>(() => EncryptionUtility.AESDecryptByteToByte(null, key));
            Assert.Throws<ArgumentNullException>(() => EncryptionUtility.AESEncryptStringToString(null, key));
            Assert.Throws<ArgumentNullException>(() => EncryptionUtility.AESEncryptStringToString("text", null));
        }

        [Test]
        public void Xor_SelfInverse_RoundtripRestoresOriginal()
        {
            byte[] original = new byte[512];
            new Random(20261006).NextBytes(original);
            byte[] code = { 0x5A, 0xA5, 0x33 };

            byte[] once = EncryptionUtility.GetXorBytes(original, code);
            byte[] twice = EncryptionUtility.GetXorBytes(once, code);

            Assert.AreEqual(original, twice, "同一 code 异或两次必须还原原文");
            CollectionAssert.AreNotEqual(original, once, "一次异或必须真的改写数据");

            // Self 变体原地改写、不另分配：改写结果应与非 Self 版一致
            byte[] selfBuffer = (byte[])original.Clone();
            EncryptionUtility.GetSelfXorBytes(selfBuffer, code);
            CollectionAssert.AreEqual(once, selfBuffer, "Self 变体必须与非 Self 版改写结果一致");
        }

        [Test]
        public void QuickXor_BeyondQuickLength_TailStaysUntouched()
        {
            // 快速版只覆盖前 QUICK_ENCRYPT_LENGTH（220）字节：这是「快速」契约的一半，越界尾部必须原样
            byte[] original = new byte[512];
            new Random(20261007).NextBytes(original);
            byte[] code = { 0x11, 0x22, 0x33, 0x44 };

            byte[] quick = EncryptionUtility.GetQuickXorBytes(original, code);

            for (int i = 0; i < EncryptionUtility.QUICK_ENCRYPT_LENGTH; i++)
            {
                Assert.AreNotEqual(original[i], quick[i], "前 220 字节必须被异或");
            }

            for (int i = EncryptionUtility.QUICK_ENCRYPT_LENGTH; i < original.Length; i++)
            {
                Assert.AreEqual(original[i], quick[i], "超出快速长度的尾部必须保持原样");
            }
        }

        [Test]
        public void AesKeyGeneration_PadTruncateAndEmpty_FollowLengthContract()
        {
            byte[] shortKey = EncryptionUtility.Generate16BytesAESKey("abc");
            Assert.AreEqual(16, shortKey.Length, "密钥必须补齐到 16 字节");
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("abc"), shortKey[..3], "前缀必须是原始密钥字节");
            CollectionAssert.AreEqual(new byte[13], shortKey[3..], "不足部分必须右补零");

            byte[] longKey = EncryptionUtility.Generate32BytesAESKey("0123456789abcdefghijklm");
            Assert.AreEqual(32, longKey.Length, "超长输入必须截断到 32 字节");
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("0123456789abcdef"), longKey[..16], "截断必须取前缀");

            byte[] empty = EncryptionUtility.Generate8BytesAESKey(string.Empty);
            Assert.IsEmpty(empty, "空串必须返回空数组（调用方自行判空）");
        }

        [Test]
        public void HmacSHA256_SameInputsSameKey_DeterministicAndKeySensitive()
        {
            string first = EncryptionUtility.HmacSHA256("payload", "secret");
            string second = EncryptionUtility.HmacSHA256("payload", "secret");
            string otherKey = EncryptionUtility.HmacSHA256("payload", "other-secret");

            Assert.AreEqual(first, second, "同输入同密钥必须产出确定性的十六进制摘要");
            Assert.AreNotEqual(first, otherKey, "换密钥必须得到不同摘要");
            StringAssert.DoesNotContain("-", first, "输出必须是连字符剥离的十六进制");
        }

        [Test]
        public void XorEncryptDecrypt_PairRestoresOriginal()
        {
            byte[] payload = Encoding.UTF8.GetBytes("xor-pair-check");
            byte[] key = { 0x9C, 0x3B, 0x77 };

            byte[] encrypted = EncryptionUtility.XorEncrypt(payload, key);
            byte[] restored = EncryptionUtility.XorDecrypt(encrypted, key);

            Assert.AreEqual(payload, restored, "XorEncrypt/XorDecrypt 必须互逆");
        }
    }
}
