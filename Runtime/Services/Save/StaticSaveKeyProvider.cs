using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 静态密钥提供方（默认）：固定口令 + 盐文经 PBKDF2-SHA256 派生密钥材料——与 V2 既有加密行为逐参一致（旧档可直接读回）。
    /// <para>SECURITY: 上线前必须替换占位口令与盐文（可在 Inspector 序列化配置，或运行期经 <see cref="Configure"/> 注入——如按平台账号派生）。</para>
    /// </summary>
    [Serializable]
    public class StaticSaveKeyProvider : SaveKeyProvider
    {
        [Tooltip("SECURITY: Must be changed to a unique, per-project secret before shipping.")]
        [SerializeField] private string m_Passphrase = SaveEncryptor.DefaultPassphrase;

        [Tooltip("SECURITY: Must be changed to a unique, per-project salt before shipping.")]
        [SerializeField] private string m_Salt = SaveEncryptor.DefaultSalt;

        [Tooltip("PBKDF2-SHA256 迭代次数：越高抗暴力破解越强，代价是首次派生耗时线性增长（派生结果按参数缓存）。")]
        [MinValue(1000)]
        [SerializeField] private int m_Iterations = SaveEncryptor.DefaultIterations;

        /// <summary>派生材料缓存（volatile 引用整体替换原子读；参数变更经 Matches 失配自动失效）。</summary>
        [NonSerialized] private volatile DerivedMaterial _cache;

        /// <summary>
        /// 共享默认实例（占位参数——等价于 V2 未配置行为；未初始化/未配置路径回退使用）。
        /// </summary>
        internal static readonly StaticSaveKeyProvider Default = new StaticSaveKeyProvider();

        /// <summary>
        /// 当前口令（只读；供处理器 <c>Key</c> 属性桥接回读）。
        /// </summary>
        internal string Passphrase => m_Passphrase;

        /// <summary>
        /// 运行期覆盖派生参数（主线程/编辑期调用；参数变更后下次取材料自动重派生）。
        /// </summary>
        /// <param name="passphrase">口令。</param>
        /// <param name="salt">盐文。</param>
        /// <param name="iterations">PBKDF2 迭代次数。</param>
        public void Configure(string passphrase, string salt, int iterations)
        {
            m_Passphrase = passphrase;
            m_Salt = salt;
            m_Iterations = iterations;
        }

        /// <summary>
        /// 获取密钥材料（PBKDF2 派生，同参数命中缓存无锁复用）。
        /// </summary>
        /// <param name="encryptionKey">成功时的加密密钥（32 字节）。</param>
        /// <param name="macKey">成功时的认证密钥（32 字节）。</param>
        /// <returns>错误码。</returns>
        public override SaveError TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey)
        {
            DerivedMaterial snapshot = _cache;
            if (snapshot == null || !snapshot.Matches(m_Passphrase, m_Salt, m_Iterations))
            {
                // 锁外派生：并发同参各自派生等值结果，后到者覆盖安装等值结果
                snapshot = new DerivedMaterial(m_Passphrase, m_Salt, m_Iterations,
                    SaveEncryptor.DeriveKeyMaterial(m_Passphrase, m_Salt, m_Iterations));
                _cache = snapshot;
            }

            encryptionKey = snapshot.EncryptionKey;
            macKey = snapshot.MacKey;
            return SaveError.None;
        }
    }
}
