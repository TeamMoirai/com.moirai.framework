using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Scene
{
    [FrameworkSetting("[服务]场景设置", "场景加载后端配置", -420)]
    public sealed class SceneServiceSettings : FrameworkSettings<SceneServiceSettings>
    {
        [InfoBox("默认场景加载处理器后端。可替换为自定义场景管理实现。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private SceneServiceHandler m_SceneServiceHandler = SceneService.CreateDefaultHandler();
        /// <summary>
        /// 默认场景加载处理器后端。
        /// </summary>
        public static SceneServiceHandler SceneServiceHandler => Instance.m_SceneServiceHandler;
    }
}