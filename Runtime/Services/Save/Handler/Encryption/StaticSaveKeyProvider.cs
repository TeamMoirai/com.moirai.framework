using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 静态密钥提供方（默认）：固定口令 + 盐文经 PBKDF2-SHA256 派生密钥材料。
    /// <para>SECURITY: 上线前必须替换占位口令与盐文（可在 Inspector 序列化配置，或运行期经 <see cref="SetDerivationParameters"/> 注入——如按平台账号派生）。</para>
    /// <para>运行期注入只写 <see cref="NonSerialized"/> 覆盖字段——序列化配置保持为构建期基线，运行期覆盖不脏化设置资产（编辑器下不会被误序列化回写）。</para>
    /// </summary>
    [Serializable]
    public class StaticSaveKeyProvider : SaveKeyProvider
    {
        [Tooltip("SECURITY：发布前必须将其修改为每个项目唯一的密钥。")]
        [SerializeField] private string m_Passphrase = SaveEncryptor.DEFAULT_PASSPHRASE;

        [Tooltip("SECURITY：发布前必须将其修改为每个项目唯一的盐值。")]
        [SerializeField] private string m_Salt = SaveEncryptor.DEFAULT_SALT;

        [Tooltip("PBKDF2-SHA256 迭代次数：越高抗暴力破解越强，代价是首次派生耗时线性增长（派生结果按参数缓存）。")]
        [MinValue(1000)]
        [SerializeField] private int m_Iterations = SaveEncryptor.DEFAULT_ITERATIONS;

        /// <summary>运行期口令覆盖（<see cref="SetDerivationParameters"/> 注入；<c>null</c> = 用序列化配置值）。</summary>
        [NonSerialized] private string _passphraseOverride;

        /// <summary>运行期盐文覆盖（<c>null</c> = 用序列化配置值）。</summary>
        [NonSerialized] private string _saltOverride;

        /// <summary>运行期迭代次数覆盖（0 = 用序列化配置值）。</summary>
        [NonSerialized] private int _iterationsOverride;

        /// <summary>派生材料缓存（volatile 引用整体替换原子读；参数变更经 Matches 失配自动失效）。</summary>
        [NonSerialized] private volatile DerivedMaterial _cache;

        /// <summary>
        /// 共享默认实例（占位参数；未配置密钥提供方时的运行期回退）。
        /// </summary>
        internal static readonly StaticSaveKeyProvider Default = new StaticSaveKeyProvider();

        /// <summary>
        /// 当前生效口令（运行期覆盖优先于序列化配置；供测试与调试回读）。
        /// </summary>
        internal string Passphrase => EffectivePassphrase;

        /// <summary>生效口令（覆盖优先）。</summary>
        private string EffectivePassphrase => _passphraseOverride ?? m_Passphrase;

        /// <summary>生效盐文（覆盖优先）。</summary>
        private string EffectiveSalt => _saltOverride ?? m_Salt;

        /// <summary>生效迭代次数（覆盖优先）。</summary>
        private int EffectiveIterations => _iterationsOverride > 0 ? _iterationsOverride : m_Iterations;

        /// <summary>
        /// 运行期覆盖派生参数（主线程/编辑期调用；仅写运行期覆盖字段——不脏化序列化配置；参数变更后下次取材料自动重派生）。
        /// </summary>
        /// <param name="passphrase">口令。</param>
        /// <param name="salt">盐文。</param>
        /// <param name="iterations">PBKDF2 迭代次数。</param>
        public void SetDerivationParameters(string passphrase, string salt, int iterations)
        {
            _passphraseOverride = passphrase;
            _saltOverride = salt;
            _iterationsOverride = iterations;
        }

        /// <summary>
        /// 获取密钥材料（PBKDF2 派生，同参数命中缓存无锁复用）。
        /// </summary>
        /// <param name="encryptionKey">成功时的加密密钥（32 字节）。</param>
        /// <param name="macKey">成功时的认证密钥（32 字节）。</param>
        /// <returns>错误码。</returns>
        public override SaveError TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey)
        {
            string passphrase = EffectivePassphrase;
            string salt = EffectiveSalt;
            int iterations = EffectiveIterations;
            DerivedMaterial snapshot = _cache;
            if (snapshot == null || !snapshot.Matches(passphrase, salt, iterations))
            {
                // 锁外派生：并发同参各自派生等值结果，后到者覆盖安装等值结果
                snapshot = new DerivedMaterial(passphrase, salt, iterations,
                    SaveEncryptor.DeriveKeyMaterial(passphrase, salt, iterations));
                _cache = snapshot;
            }

            encryptionKey = snapshot.EncryptionKey;
            macKey = snapshot.MacKey;
            return SaveError.None;
        }
    }
}
