using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 口令密钥提供方：运行期注入的玩家口令经 PBKDF2-SHA256 派生密钥材料（密码锁存档场景）。
    /// <para>口令只存内存（绝不序列化落盘）；未注入时 <see cref="TryGetKeyMaterial"/> 返回 <see cref="SaveError.InvalidArgument"/>——
    /// 存档写路径随之 fail-fast（GameException），读路径判别为参数错误。</para>
    /// </summary>
    [Serializable]
    public class PassphraseSaveKeyProvider : SaveKeyProvider
    {
        [Tooltip("PBKDF2 盐文（口令派生用；项目级固定值，非玩家口令）。")]
        [SerializeField] private string m_Salt = SaveEncryptor.DEFAULT_SALT;

        [Tooltip("PBKDF2-SHA256 迭代次数（派生结果按口令缓存）。")]
        [MinValue(1000)]
        [SerializeField] private int m_Iterations = SaveEncryptor.DEFAULT_ITERATIONS;

        /// <summary>运行期口令（仅内存，绝不序列化）。</summary>
        [NonSerialized] private string _passphrase;

        /// <summary>派生材料缓存（口令变更经 Matches 失配自动失效）。</summary>
        [NonSerialized] private volatile DerivedMaterial _cache;

        /// <summary>
        /// 是否已注入运行期口令。
        /// </summary>
        public bool HasPassphrase => !string.IsNullOrEmpty(_passphrase);

        /// <inheritdoc />
        /// <para>只看序列化的盐文：口令是运行期注入的（出厂即空，空不等于占位），
        /// 盐文仍随包发布——占位盐会削弱口令派生的抗暴力性。</para>
        internal override bool UsesPlaceholderCredentials => IsFactoryPlaceholder(m_Salt);

        /// <summary>
        /// 注入运行期口令（同口令重复注入命中既有缓存）。
        /// </summary>
        /// <param name="passphrase">玩家口令。</param>
        public void SetPassphrase(string passphrase)
        {
            _passphrase = passphrase;
        }

        /// <summary>
        /// 清除运行期口令与派生缓存（退出登录/切换账号时调用）。
        /// </summary>
        public void ClearPassphrase()
        {
            _passphrase = null;
            _cache = null;
        }

        /// <summary>
        /// 获取密钥材料（未注入口令返回 <see cref="SaveError.InvalidArgument"/>）。
        /// </summary>
        /// <param name="encryptionKey">成功时的加密密钥（32 字节）。</param>
        /// <param name="macKey">成功时的认证密钥（32 字节）。</param>
        /// <returns>错误码。</returns>
        public override SaveError TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey)
        {
            encryptionKey = null;
            macKey = null;
            if (!HasPassphrase)
            {
                return SaveError.InvalidArgument;
            }

            DerivedMaterial snapshot = _cache;
            if (snapshot == null || !snapshot.Matches(_passphrase, m_Salt, m_Iterations))
            {
                snapshot = new DerivedMaterial(_passphrase, m_Salt, m_Iterations,
                    SaveEncryptor.DeriveKeyMaterial(_passphrase, m_Salt, m_Iterations));
                _cache = snapshot;
            }

            encryptionKey = snapshot.EncryptionKey;
            macKey = snapshot.MacKey;
            return SaveError.None;
        }
    }
}
