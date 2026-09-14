using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档密钥提供方抽象基类（框架插拔件惯例：<see cref="SaveServiceSettings"/> 以 [SerializeReference] + ProviderDropdown 持有实例）。
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
