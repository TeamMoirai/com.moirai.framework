using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    [FrameworkSetting("[服务]存档设置", "存档格式与加密配置", -410)]
    public class SaveServiceSettings : FrameworkSettings<SaveServiceSettings>
    {
        [InfoBox("加密处理器使用下方密钥。SECURITY: 发布前必须替换为项目专属密钥。", InfoMessageType.None, nameof(IsEncryptedConfig))]
        [ProviderDropdown]
        [SerializeReference] private SaveServiceHandlerConfig m_HandlerConfig = new JsonSaveHandlerConfig();

        [ShowIf(nameof(IsEncryptedConfig))]
        // SECURITY: Must be changed to a unique, per-project secret before shipping.
        [SerializeField] private string m_EncryptionKey = "CHANGE_ME_BEFORE_SHIPPING";

        [SerializeField] private string m_SaveFileExtension = ".sav";

        private bool IsEncryptedConfig => m_HandlerConfig is { IsEncrypted: true };

        /// <summary>
        /// 存档后端配置（纯数据，经 <see cref="SaveServiceHandlerConfig.CreateHandler"/> 创建处理器实例，可替换序列化/加密策略）。
        /// </summary>
        public static SaveServiceHandlerConfig SaveServiceHandlerConfig => Instance.m_HandlerConfig;

        public static string EncryptionKey => Instance.m_EncryptionKey; // TODO 使用用户ID加密？
        public static string SaveFileExtension => Instance.m_SaveFileExtension;

        private void Reset()
        {
            m_HandlerConfig = new JsonSaveHandlerConfig();
        }
    }
}
