using System;
using System.IO;
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
    internal class AESEncryptedSaveHandler : SaveServiceHandler
    {
        [NonSerialized] private SaveEncryptor _encryptor;

        [InfoBox("须配置密钥提供方（推荐 StaticSaveKeyProvider，并替换占位口令/盐文）。未配置时回退占位默认静态密钥，SECURITY: 发布前必须替换。", InfoMessageType.Warning, nameof(ShowMissingKeyProviderWarning))]
        [InfoBox("口令或盐文仍是包内出厂占位值：拿到包的人能派生同一把密钥，加密等价于不加密。SECURITY: 发布前替换为项目专属值（出包时 SaveSettingsBuildValidator 会再报一次）。", InfoMessageType.Error, nameof(ShowPlaceholderKeyWarning))]
        [Tooltip("密钥提供方：加密密钥来源（空 = 回退 StaticSaveKeyProvider.Default 占位默认）。推荐配置 StaticSaveKeyProvider 并替换占位口令/盐文；口令注入 / HKDF 按用户派生等进阶策略在此接入。")]
        [ProviderDropdown]
        [SerializeReference] private SaveKeyProvider m_KeyProvider = StaticSaveKeyProvider.Default;
        /// <summary>密钥提供方未配置时显示告警（运行期回退占位默认静态密钥）。</summary>
        private bool ShowMissingKeyProviderWarning => m_KeyProvider == null;

        /// <summary>密钥提供方的生效材料仍是出厂占位值时显示告警（不限静态提供方——HKDF 主密钥、口令盐文同理）。</summary>
        private bool ShowPlaceholderKeyWarning =>
            m_KeyProvider is SaveKeyProvider provider && provider.UsesPlaceholderCredentials;

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
        /// <summary>
        /// 载荷写流包装：叠加 AES-256-CBC + HMAC 加密链（明文经返回流写入即加密落存储目标流，关闭收尾末块与 MAC 尾）。
        /// </summary>
        /// <param name="target">存储目标流（载荷 CRC 包装层）。</param>
        /// <returns>加密写入流（只写；调用方负责关闭以收尾加密帧）。</returns>
        protected internal sealed override Stream OpenPayloadWriteStream(Stream target)
        {
            SaveError keyError = KeyProvider.TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey);
            if (keyError != SaveError.None)
            {
                throw new GameException(StringUtility.Format("Save encryption key unavailable, error: {0}.", keyError));
            }

            return Encryptor.OpenEncryptStreamWithMaterial(target, encryptionKey, macKey);
        }

        /// <summary>
        /// 载荷读流包装：叠加 AES-256-CBC + HMAC 解密链（源流读取即解密，读尽时 HMAC 定稿比对）。
        /// </summary>
        /// <param name="source">存储载荷源流（载荷 CRC 增量包装层）。</param>
        /// <param name="payloadLength">存储载荷总字节数（文件头口径）。</param>
        /// <returns>解密读流（只读；调用方负责关闭）。</returns>
        protected internal sealed override Stream OpenPayloadReadStream(Stream source, long payloadLength)
        {
            SaveError keyError = KeyProvider.TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey);
            if (keyError != SaveError.None)
            {
                throw new SaveEncryptor.SaveDecryptStreamException(keyError, "Save decryption key unavailable.");
            }

            return Encryptor.OpenDecryptStreamWithMaterial(source, payloadLength, encryptionKey, macKey);
        }
    }
}
