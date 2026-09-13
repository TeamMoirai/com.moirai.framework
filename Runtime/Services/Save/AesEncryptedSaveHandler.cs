using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// AES 加密存档处理器：容器字节经 <see cref="SaveEncryptor"/>（AES-256-CBC + HMAC，encrypt-then-MAC）变换后存储。
    /// <para>密钥材料来源由 <see cref="ISaveKeyProvider"/> 提供（<see cref="OnInit"/> 从 <see cref="SaveServiceSettings"/> 解析，
    /// 默认 <see cref="StaticSaveKeyProvider"/> 沿用 V2 静态口令 PBKDF2 语义——同参派生结果逐位一致，旧档可直接读回）；
    /// 派生材料由提供方按参数缓存。</para>
    /// </summary>
    [Serializable]
    public class AesEncryptedSaveHandler : SaveServiceHandler
    {
        [NonSerialized] private SaveEncryptor _encryptor;

        /// <summary>密钥提供方（<see cref="OnInit"/> 主线程从设置解析；测试可直接赋值注入——纯 .NET，工作线程调用安全）。</summary>
        [NonSerialized] internal SaveKeyProvider _keyProvider;

        /// <summary><see cref="Key"/> 属性桥接的显式口令提供方（设置 Key 时创建；优先于 OnInit 解析结果，行为等价 V2 直接设 Encryptor.Key）。</summary>
        [NonSerialized] private StaticSaveKeyProvider _explicitKeyProvider;

        /// <summary>
        /// AES 加密器（懒加载；仅承担 AES/HMAC 机件，密钥材料经 <see cref="ISaveKeyProvider"/> 直给）。
        /// </summary>
        private SaveEncryptor Encryptor => _encryptor ??= new SaveEncryptor();

        /// <summary>
        /// 保存和加载文件的密钥（静态口令桥接：写入即切换为显式口令提供方；读取回显当前静态口令，未设置时为占位默认值）。
        /// <para>与用户/设备绑定的进阶密钥策略请改用 <see cref="SaveServiceSettings"/> 的密钥提供方配置（口令注入 / HKDF 按用户派生）。</para>
        /// </summary>
        protected internal string Key
        {
            get => _explicitKeyProvider != null
                ? _explicitKeyProvider.Passphrase
                : (_keyProvider as StaticSaveKeyProvider)?.Passphrase ?? SaveEncryptor.DefaultPassphrase;
            set
            {
                StaticSaveKeyProvider provider = new StaticSaveKeyProvider();
                provider.Configure(value, SaveEncryptor.DefaultSalt, SaveEncryptor.DefaultIterations);
                _explicitKeyProvider = provider;
            }
        }

        /// <summary>
        /// 密钥提供方（显式口令优先，其次 OnInit 解析结果，兜底占位默认——全程不触达 <see cref="SaveServiceSettings"/>，工作线程安全）。
        /// </summary>
        private SaveKeyProvider KeyProvider => _explicitKeyProvider ?? _keyProvider ?? StaticSaveKeyProvider.Default;

        /// <summary>
        /// 初始化时从 <see cref="SaveServiceSettings"/> 解析密钥提供方（未配置时按 V2 语义以静态密钥参数构建；显式 <see cref="Key"/> 注入优先）。
        /// </summary>
        protected override void OnInit()
        {
            base.OnInit();
            if (_explicitKeyProvider == null)
            {
                _keyProvider = SaveServiceSettings.KeyProvider;
                if (_keyProvider == null)
                {
                    // 未配置密钥提供方 = V2 静态密钥语义（口令/迭代次数沿用设置项，盐文为加密器默认占位）
                    StaticSaveKeyProvider staticProvider = new StaticSaveKeyProvider();
                    staticProvider.Configure(SaveServiceSettings.EncryptionKey, SaveEncryptor.DefaultSalt, SaveServiceSettings.Pbkdf2Iterations);
                    _keyProvider = staticProvider;
                }
            }
        }

        /// <summary>
        /// 载荷变换：容器字节加密为存储载荷（区间直通 <see cref="SaveEncryptor"/>，无二次拷贝）。
        /// </summary>
        /// <param name="container">容器字节视图。</param>
        /// <param name="payload">存储载荷视图。</param>
        /// <returns>错误码。</returns>
        protected internal sealed override SaveError OnTransformContainer(SaveBufferSegment container, out SaveBufferSegment payload)
        {
            SaveError keyError = KeyProvider.TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey);
            if (keyError != SaveError.None)
            {
                payload = default;
                return keyError;
            }

            SaveError error = Encryptor.TryEncryptWithMaterial(container.Buffer, container.Offset, container.Length, encryptionKey, macKey, out byte[] encrypted);
            payload = encrypted != null ? SaveBufferSegment.FromExact(encrypted) : default;
            return error;
        }

        /// <summary>
        /// 载荷还原：存储载荷验证并解密为容器字节（区间直通 <see cref="SaveEncryptor"/>，无二次拷贝）。
        /// </summary>
        /// <param name="payload">存储载荷视图。</param>
        /// <param name="container">成功时的容器字节视图。</param>
        /// <returns>错误码（<see cref="SaveError.IntegrityCheckFailed"/> = 疑似篡改；<see cref="SaveError.DecryptionFailed"/> = 密钥不匹配/密文非法）。</returns>
        protected internal sealed override SaveError OnRestorePayload(SaveBufferSegment payload, out SaveBufferSegment container)
        {
            SaveError keyError = KeyProvider.TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey);
            if (keyError != SaveError.None)
            {
                container = default;
                return keyError;
            }

            SaveError error = Encryptor.TryDecryptWithMaterial(payload.Buffer, payload.Offset, payload.Length, encryptionKey, macKey, out byte[] decrypted);
            container = decrypted != null ? SaveBufferSegment.FromExact(decrypted) : default;
            return error;
        }
    }
}
