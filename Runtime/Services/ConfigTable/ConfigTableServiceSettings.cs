using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    [FrameworkSetting("[服务]配置表设置", "配置表后端选择", -510)]
    public sealed class ConfigTableServiceSettings : FrameworkSettings<ConfigTableServiceSettings>
    {
        [InfoBox("默认使用兜底实现（记录错误并返回空结果）。游戏侧生成代码后应替换为自定义处理器。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private ConfigTableServiceHandler m_ConfigTableServiceHandler = ConfigTableService.CreateDefaultHandler();
        /// <summary>配置表处理器（后端）。</summary>
        internal static ConfigTableServiceHandler ConfigTableServiceHandler
        {
            get => Instance.m_ConfigTableServiceHandler;
            private set => Instance.m_ConfigTableServiceHandler = value;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器注入自定义实现
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void InjectConfigTableHandler<T>() where T : ConfigTableServiceHandler, new()
        {
            if (ConfigTableServiceHandler is null or DefaultConfigTableHandler)
            {
                ConfigTableServiceHandler = new T();
                UnityEditor.EditorUtility.SetDirty(Instance);
            }
        }
#endif
    }
}