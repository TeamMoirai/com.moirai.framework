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
        /// 编辑器注入自定义实现。
        /// </summary>
        public static void InjectConfigTableHandler<T>() where T : ConfigTableServiceHandler, new()
        {
            // 导入窗口内设置资产可能还没进资产库，Instance 按「暂不可用」回 null（见 FrameworkSettings.Loader）。
            // 本钩子每次脚本重载都跑，本次跳过不算丢注入，下一次补上。
            if (Instance is null)
            {
                Debug.LogWarning($"ConfigTableServiceSettings 资产当前不可用，本次跳过 {typeof(T).Name} 的处理器注入，下次脚本重载重试。");
                return;
            }

            if (ConfigTableServiceHandler is null or DefaultConfigTableHandler)
            {
                ConfigTableServiceHandler = new T();
                UnityEditor.EditorUtility.SetDirty(Instance);
            }
        }
#endif
    }
}