using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务设置：存档处理器（存储管线策略）、存储后端（IO 下沉目标）、压缩提供方、默认序列化后端、加密密钥、PBKDF2 迭代次数与文件扩展名。
    /// </summary>
    [FrameworkSetting("[服务]存档设置", "存档格式与加密配置", -410)]
    public class SaveServiceSettings : FrameworkSettings<SaveServiceSettings>
    {
        [InfoBox("加密处理器使用下方密钥与派生参数。SECURITY: 发布前必须替换为项目专属密钥与盐文（盐文由加密器内占位值提供，可按需覆盖）。", InfoMessageType.None, nameof(ShowLegacyKeyFields))]
        [ProviderDropdown]
        [SerializeReference] private SaveServiceHandler m_SaveServiceHandler = new PlainSaveHandler();

        [Tooltip("存储后端：存档 IO 的下沉目标（默认本地文件；云存档等自定义后端继承 SaveStorageBackend 接入）。置空时回退本地文件后端。")]
        [ProviderDropdown]
        [SerializeReference] private SaveStorageBackend m_StorageBackend = new FileSaveStorageBackend();

        [Tooltip("压缩提供方：容器字节在加密前压缩（空 = 不压缩）。写出档的文件头记录提供方 ID；自定义提供方须注册到 SaveCompressionRegistry 才能读回旧档。")]
        [ProviderDropdown]
        [SerializeReference] private SaveCompressionProvider m_CompressionProvider;

        [ShowIf(nameof(IsEncryptedHandler))]
        [Tooltip("密钥提供方：加密密钥来源（空 = 静态密钥，使用下方密钥与派生参数）。运行期口令注入 / HKDF 按用户派生等进阶策略在此接入。")]
        [ProviderDropdown]
        [SerializeReference] private SaveKeyProvider m_KeyProvider;

        [Tooltip("默认序列化后端：未显式声明后端的数据块（无 SaveDataAttribute）使用该后端。二进制后端要求项目已引入对应 NuGet 包。")]
        [SerializeField] private ESaveBackend m_DefaultBackend = ESaveBackend.Json;

        [ShowIf(nameof(ShowLegacyKeyFields))]
        // SECURITY: Must be changed to a unique, per-project secret before shipping.
        [SerializeField] private string m_EncryptionKey = "CHANGE_ME_BEFORE_SHIPPING";

        [ShowIf(nameof(ShowLegacyKeyFields))]
        [Tooltip("PBKDF2-SHA256 迭代次数：越高抗暴力破解越强，代价是每次存/读档的派生耗时线性增长（派生结果按处理器实例缓存）。")]
        [MinValue(1000)]
        [SerializeField] private int m_Pbkdf2Iterations = SaveEncryptor.DefaultIterations;

        [SerializeField] private string m_SaveFileExtension = ".sav";

        private bool IsEncryptedHandler => m_SaveServiceHandler is AesEncryptedSaveHandler;

        /// <summary>旧版静态密钥字段可见性（配置自定义密钥提供方后隐藏——密钥来源以提供方为准）。</summary>
        private bool ShowLegacyKeyFields => IsEncryptedHandler && m_KeyProvider == null;

        /// <summary>
        /// 存档处理器实例（由 Inspector 序列化配置，可替换存储管线策略）。
        /// </summary>
        public static SaveServiceHandler SaveServiceHandler => Instance.m_SaveServiceHandler;

        /// <summary>
        /// 存储后端实例（由 Inspector 序列化配置，可替换 IO 下沉目标；未配置时为 <c>null</c>，由处理器回退本地文件后端）。
        /// </summary>
        public static SaveStorageBackend StorageBackend => Instance.m_StorageBackend;

        /// <summary>
        /// 压缩提供方实例（由 Inspector 序列化配置；<c>null</c> = 不压缩）。
        /// </summary>
        public static SaveCompressionProvider CompressionProvider => Instance.m_CompressionProvider;

        /// <summary>
        /// 密钥提供方实例（由 Inspector 序列化配置；<c>null</c> = 静态密钥，沿用 <see cref="EncryptionKey"/> 与 <see cref="Pbkdf2Iterations"/>）。
        /// </summary>
        public static SaveKeyProvider KeyProvider => Instance.m_KeyProvider;

        /// <summary>
        /// 默认序列化后端（未显式声明后端的数据块使用该后端）。
        /// </summary>
        public static ESaveBackend DefaultBackend => Instance.m_DefaultBackend;

        /// <summary>
        /// 加密密钥（加密处理器在初始化期注入；上线前必须改为项目专属密钥）。
        /// <para>与用户/设备绑定的进阶密钥策略（如按平台账号派生）经密钥提供方（<see cref="KeyProvider"/>）接入。</para>
        /// </summary>
        public static string EncryptionKey => Instance.m_EncryptionKey;

        /// <summary>
        /// PBKDF2-SHA256 迭代次数（加密处理器在初始化期注入）。
        /// </summary>
        public static int Pbkdf2Iterations => Instance.m_Pbkdf2Iterations;

        /// <summary>
        /// 存档文件扩展名（保存时取 fileName 去扩展名部分后重新追加）。
        /// </summary>
        public static string SaveFileExtension => Instance.m_SaveFileExtension;

        private void Reset()
        {
            m_SaveServiceHandler = new PlainSaveHandler();
            m_StorageBackend = new FileSaveStorageBackend();
            m_CompressionProvider = null;
            m_KeyProvider = null;
            m_DefaultBackend = ESaveBackend.Json;
            m_Pbkdf2Iterations = SaveEncryptor.DefaultIterations;
            m_SaveFileExtension = ".sav";
        }
    }
}
