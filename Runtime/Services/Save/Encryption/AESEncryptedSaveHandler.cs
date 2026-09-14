using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// AES 加密存档处理器：容器字节经 <see cref="SaveEncryptor"/>（AES-256-CBC + HMAC，encrypt-then-MAC）变换后存储。
    /// <para>密钥材料来源由处理器内嵌的 <see cref="SaveKeyProvider"/> 提供（空 = 回退 <see cref="StaticSaveKeyProvider.Default"/> 占位默认——
    /// 上线前须在 Inspector 配置项目专属密钥提供方）。派生材料由提供方按参数缓存；提供方须为纯 .NET，工作线程调用安全。</para>
    /// </summary>
    [Serializable]
    // ReSharper disable once InconsistentNaming
    public class AESEncryptedSaveHandler : SaveServiceHandler
    {
        [NonSerialized] private SaveEncryptor _encryptor;

        [InfoBox("须配置密钥提供方（推荐 StaticSaveKeyProvider，并替换占位口令/盐文）。未配置时回退占位默认静态密钥，SECURITY: 发布前必须替换。", InfoMessageType.Warning, nameof(ShowMissingKeyProviderWarning))]
        [Tooltip("密钥提供方：加密密钥来源（空 = 回退 StaticSaveKeyProvider.Default 占位默认）。推荐配置 StaticSaveKeyProvider 并替换占位口令/盐文；口令注入 / HKDF 按用户派生等进阶策略在此接入。")]
        [ProviderDropdown]
        [SerializeReference] private SaveKeyProvider m_KeyProvider = StaticSaveKeyProvider.Default;
        /// <summary>密钥提供方未配置时显示告警（运行期回退占位默认静态密钥）。</summary>
        private bool ShowMissingKeyProviderWarning => m_KeyProvider == null;

        /// <summary>密钥提供方注入点（测试/代码装配用；Inspector 配置走序列化字段——纯 .NET，工作线程调用安全）。</summary>
        internal SaveKeyProvider KeyProvider
        {
            get => m_KeyProvider ?? StaticSaveKeyProvider.Default;
            set => m_KeyProvider = value;
        }

        /// <summary>
        /// AES 加密器（懒加载；仅承担 AES/HMAC 机件，密钥材料经 <see cref="ISaveKeyProvider"/> 直给）。
        /// </summary>
        private SaveEncryptor Encryptor => _encryptor ??= new SaveEncryptor();
        
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
