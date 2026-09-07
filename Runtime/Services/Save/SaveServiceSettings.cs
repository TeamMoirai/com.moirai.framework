using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务设置：存档处理器（存储管线策略）、默认序列化后端、加密密钥、PBKDF2 迭代次数与文件扩展名。
    /// </summary>
    [FrameworkSetting("[服务]存档设置", "存档格式与加密配置", -410)]
    public class SaveServiceSettings : FrameworkSettings<SaveServiceSettings>
    {
        [InfoBox("加密处理器使用下方密钥与派生参数。SECURITY: 发布前必须替换为项目专属密钥与盐文（盐文由加密器内占位值提供，可按需覆盖）。", InfoMessageType.None, nameof(IsEncryptedHandler))]
        [ProviderDropdown]
        [SerializeReference] private SaveServiceHandler m_SaveServiceHandler = new PlainSaveHandler();

        [Tooltip("默认序列化后端：未显式声明后端的数据块（无 SaveDataAttribute）使用该后端。二进制后端要求项目已引入对应 NuGet 包。")]
        [SerializeField] private ESaveBackend m_DefaultBackend = ESaveBackend.Json;

        [ShowIf(nameof(IsEncryptedHandler))]
        // SECURITY: Must be changed to a unique, per-project secret before shipping.
        [SerializeField] private string m_EncryptionKey = "CHANGE_ME_BEFORE_SHIPPING";

        [ShowIf(nameof(IsEncryptedHandler))]
        [Tooltip("PBKDF2-SHA256 迭代次数：越高抗暴力破解越强，代价是每次存/读档的派生耗时线性增长（派生结果按处理器实例缓存）。")]
        [MinValue(1000)]
        [SerializeField] private int m_Pbkdf2Iterations = SaveEncryptor.DefaultIterations;

        [SerializeField] private string m_SaveFileExtension = ".sav";

        private bool IsEncryptedHandler => m_SaveServiceHandler is AesEncryptedSaveHandler;

        /// <summary>
        /// 存档处理器实例（由 Inspector 序列化配置，可替换存储管线策略）。
        /// </summary>
        public static SaveServiceHandler SaveServiceHandler => Instance.m_SaveServiceHandler;

        /// <summary>
        /// 默认序列化后端（未显式声明后端的数据块使用该后端）。
        /// </summary>
        public static ESaveBackend DefaultBackend => Instance.m_DefaultBackend;

        /// <summary>
        /// 加密密钥（加密处理器在初始化期注入；上线前必须改为项目专属密钥）。
        /// <para>与用户/设备绑定的进阶密钥策略（如按平台账号派生）由游戏层自行实现并覆盖加密器参数。</para>
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
            m_DefaultBackend = ESaveBackend.Json;
            m_Pbkdf2Iterations = SaveEncryptor.DefaultIterations;
            m_SaveFileExtension = ".sav";
        }
    }
}
