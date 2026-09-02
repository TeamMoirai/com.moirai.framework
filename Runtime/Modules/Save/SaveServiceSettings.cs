using Sirenix.OdinInspector;
using Moirai.Atropos;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    [FrameworkSetting("[服务]存档设置", "存档格式与加密配置", -410)]
    public class SaveServiceSettings : FrameworkSettings<SaveServiceSettings>
    {
        [InfoBox("加密处理器使用下方密钥。SECURITY: 发布前必须替换为项目专属密钥。", InfoMessageType.None, nameof(IsEncryptedHandler))]
        [ProviderDropdown]
        [SerializeReference] private SaveServiceHandler m_SaveServiceHandler = new JsonSaveHandler();

        [ShowIf(nameof(IsEncryptedHandler))]
        // SECURITY: Must be changed to a unique, per-project secret before shipping.
        [SerializeField] private string m_EncryptionKey = "CHANGE_ME_BEFORE_SHIPPING";

        [SerializeField] private string m_SaveFileExtension = ".sav";

        private bool IsEncryptedHandler => m_SaveServiceHandler is EncryptedSaveHandlerBase;

        /// <summary>
        /// 存档处理器实例（由 Inspector 序列化配置，可替换序列化/加密策略）。
        /// </summary>
        public static SaveServiceHandler SaveServiceHandler => Instance.m_SaveServiceHandler;

        public static string EncryptionKey => Instance.m_EncryptionKey; // TODO 使用用户ID加密？
        public static string SaveFileExtension => Instance.m_SaveFileExtension;

        private void Reset()
        {
            m_SaveServiceHandler = new JsonSaveHandler();
        }
    }
}
