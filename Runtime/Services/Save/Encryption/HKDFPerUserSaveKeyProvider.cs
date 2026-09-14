using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 按用户派生的密钥提供方：HKDF-SHA256（extract：主密钥 + 用户 ID 盐 → expand 64B 拆分加密/认证密钥）。
    /// <para>不同用户产出完全独立的密钥材料——多账号存档互相不可读；未设用户 ID 时以空盐派生（等价单用户默认档）。
    /// 主密钥序列化于设置资产（SECURITY: 上线前必须替换占位值）；用户 ID 仅内存（运行期注入）。</para>
    /// </summary>
    [Serializable]
    // ReSharper disable once InconsistentNaming
    public class HKDFPerUserSaveKeyProvider : SaveKeyProvider
    {
        /// <summary>HKDF expand 的 info 上下文串（域分隔）。</summary>
        private static readonly byte[] s_Info = Encoding.UTF8.GetBytes("Moirai.Save");

        [Tooltip("HKDF 主密钥。SECURITY：在发布前必须改为每个项目唯一的密钥。")]
        [SerializeField] private string m_MasterSecret = SaveEncryptor.DEFAULT_PASSPHRASE;

        /// <summary>当前用户 ID（仅内存，运行期注入；null/空 = 默认档）。</summary>
        [NonSerialized] private string _userId;

        /// <summary>派生材料缓存（主密钥/用户 ID 变更经 Matches 失配自动失效）。</summary>
        [NonSerialized] private volatile DerivedMaterial _cache;

        /// <summary>
        /// 当前用户 ID（设置后下次取材料自动按新用户重派生）。
        /// </summary>
        public string UserId
        {
            get => _userId;
            set => _userId = value;
        }

        /// <summary>
        /// 获取密钥材料（HKDF-SHA256 按用户派生，同参数命中缓存无锁复用）。
        /// </summary>
        /// <param name="encryptionKey">成功时的加密密钥（32 字节）。</param>
        /// <param name="macKey">成功时的认证密钥（32 字节）。</param>
        /// <returns>错误码。</returns>
        public override SaveError TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey)
        {
            string userId = _userId ?? string.Empty;
            DerivedMaterial snapshot = _cache;
            if (snapshot == null || !snapshot.Matches(m_MasterSecret, userId, 0))
            {
                byte[] material = HkdfSha256(
                    Encoding.UTF8.GetBytes(m_MasterSecret),
                    Encoding.UTF8.GetBytes(userId),
                    s_Info,
                    SaveEncryptor.ENCRYPTION_KEY_SIZE + SaveEncryptor.MAC_SIZE);
                snapshot = new DerivedMaterial(m_MasterSecret, userId, 0, material);
                _cache = snapshot;
            }

            encryptionKey = snapshot.EncryptionKey;
            macKey = snapshot.MacKey;
            return SaveError.None;
        }

        /// <summary>
        /// HKDF-SHA256（RFC 5869）：extract（HMAC(salt, ikm)）→ expand（T(i) 链，输出 ≤ 64B 仅需两段）。
        /// </summary>
        /// <param name="ikm">输入密钥材料。</param>
        /// <param name="salt">盐（按用户 ID）。</param>
        /// <param name="info">上下文信息串。</param>
        /// <param name="length">输出字节数（≤ 64）。</param>
        /// <returns>派生密钥材料。</returns>
        private static byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            byte[] pseudoRandomKey;
            using (HMACSHA256 hmac = new HMACSHA256(salt))
            {
                pseudoRandomKey = hmac.ComputeHash(ikm);
            }

            using (HMACSHA256 hmac = new HMACSHA256(pseudoRandomKey))
            {
                byte[] first = ComputeHkdfBlock(hmac, null, info, 1);
                if (length <= first.Length)
                {
                    byte[] truncated = new byte[length];
                    Buffer.BlockCopy(first, 0, truncated, 0, length);
                    return truncated;
                }

                byte[] second = ComputeHkdfBlock(hmac, first, info, 2);
                byte[] output = new byte[first.Length + second.Length];
                Buffer.BlockCopy(first, 0, output, 0, first.Length);
                Buffer.BlockCopy(second, 0, output, first.Length, second.Length);
                return output;
            }
        }

        /// <summary>
        /// 计算 HKDF expand 单段：HMAC(prk, T(i-1)‖info‖counter)。
        /// </summary>
        /// <param name="hmac">以 prk 为钥的 HMAC 实例。</param>
        /// <param name="previous">上一段输出（首段为 <c>null</c>）。</param>
        /// <param name="info">上下文信息串。</param>
        /// <param name="counter">段计数（1 起）。</param>
        /// <returns>段摘要。</returns>
        private static byte[] ComputeHkdfBlock(HMACSHA256 hmac, byte[] previous, byte[] info, byte counter)
        {
            int previousLength = previous?.Length ?? 0;
            byte[] input = new byte[previousLength + info.Length + 1];
            if (previousLength > 0)
            {
                Buffer.BlockCopy(previous, 0, input, 0, previousLength);
            }

            Buffer.BlockCopy(info, 0, input, previousLength, info.Length);
            input[input.Length - 1] = counter;
            return hmac.ComputeHash(input);
        }
    }
}
