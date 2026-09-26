using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档密钥提供方抽象基类（框架插拔件惯例：<see cref="AESEncryptedSaveHandler"/> 以 [SerializeReference] + ProviderDropdown 持有实例）。
    /// <para>实现 <see cref="ISaveKeyProvider"/>；派生材料缓存约定 = 不可变快照（<see cref="DerivedMaterial"/>）+ volatile 引用整体替换，
    /// 参数变更经 <see cref="DerivedMaterial.Matches"/> 失配自动失效重派生。</para>
    /// </summary>
    [Serializable]
    public abstract class SaveKeyProvider : ISaveKeyProvider
    {
        /// <summary>
        /// 获取密钥材料（失败返回错误码，输出为 <c>null</c>）。
        /// </summary>
        /// <param name="encryptionKey">成功时的加密密钥（32 字节）。</param>
        /// <param name="macKey">成功时的认证密钥（32 字节）。</param>
        /// <returns>错误码（<see cref="SaveError.None"/> 或 <see cref="SaveError.InvalidArgument"/> 等）。</returns>
        public abstract SaveError TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey);

        /// <summary>
        /// 生效密钥材料是否仍为包内出厂占位值（或为空）。
        /// <para>占位值随包发布，任何拿到包的人都能派生同一把密钥，等同不加密。判据供 Inspector 告警与构建期自检共用，
        /// 不在运行期抛——已有存档可能就是用占位值写的，拦停只会把「配置没改」升级成「存档打不开」。</para>
        /// <para>内置提供方各自覆写；第三方提供方默认不报（密钥来源自管），有出厂默认值的应覆写并委托
        /// <see cref="IsFactoryPlaceholder"/>。</para>
        /// </summary>
        internal virtual bool UsesPlaceholderCredentials => false;

        /// <summary>
        /// 是否为出厂占位材料（空串，或仍是 <see cref="SaveEncryptor.DEFAULT_PASSPHRASE"/> / <see cref="SaveEncryptor.DEFAULT_SALT"/>）。
        /// </summary>
        /// <param name="value">生效后的口令 / 盐文 / 主密钥。</param>
        /// <returns>占位或空时为真。</returns>
        protected static bool IsFactoryPlaceholder(string value)
        {
            return string.IsNullOrEmpty(value)
                || string.Equals(value, SaveEncryptor.DEFAULT_PASSPHRASE, StringComparison.Ordinal)
                || string.Equals(value, SaveEncryptor.DEFAULT_SALT, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// 派生材料不可变缓存快照（参数 + 拆分结果的不可变整体，原子读避免字段组撕裂）。
        /// </summary>
        protected sealed class DerivedMaterial
        {
            /// <summary>主密钥材料来源（口令 / 主密钥）。</summary>
            internal readonly string Secret;

            /// <summary>次要派生参数（盐文 / 用户 ID）。</summary>
            internal readonly string Secondary;

            /// <summary>迭代次数（PBKDF2 系；HKDF 系置 0）。</summary>
            internal readonly int Iterations;

            /// <summary>加密密钥（32 字节）。</summary>
            internal readonly byte[] EncryptionKey;

            /// <summary>认证密钥（32 字节）。</summary>
            internal readonly byte[] MacKey;

            /// <summary>
            /// 创建快照（64 字节材料拆分为加密/认证两个 32 字节密钥）。
            /// </summary>
            /// <param name="secret">主密钥材料来源。</param>
            /// <param name="secondary">次要派生参数。</param>
            /// <param name="iterations">迭代次数。</param>
            /// <param name="material">64 字节密钥材料。</param>
            internal DerivedMaterial(string secret, string secondary, int iterations, byte[] material)
            {
                Secret = secret;
                Secondary = secondary;
                Iterations = iterations;
                EncryptionKey = new byte[SaveEncryptor.ENCRYPTION_KEY_SIZE];
                MacKey = new byte[SaveEncryptor.MAC_SIZE];
                Buffer.BlockCopy(material, 0, EncryptionKey, 0, SaveEncryptor.ENCRYPTION_KEY_SIZE);
                Buffer.BlockCopy(material, SaveEncryptor.ENCRYPTION_KEY_SIZE, MacKey, 0, SaveEncryptor.MAC_SIZE);
            }

            /// <summary>
            /// 判断快照是否匹配当前派生参数。
            /// </summary>
            /// <param name="secret">主密钥材料来源。</param>
            /// <param name="secondary">次要派生参数。</param>
            /// <param name="iterations">迭代次数。</param>
            /// <returns>匹配返回 <c>true</c>。</returns>
            internal bool Matches(string secret, string secondary, int iterations)
            {
                return Iterations == iterations
                    && string.Equals(Secret, secret, StringComparison.Ordinal)
                    && string.Equals(Secondary, secondary, StringComparison.Ordinal);
            }
        }
    }
}
