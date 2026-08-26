using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Scene
{
    [FrameworkSetting("场景设置", "场景加载后端配置", -456)]
    public sealed class SceneSettings : FrameworkSettings<SceneSettings>
    {
        [InfoBox("默认场景加载后端。可替换为自定义场景管理实现。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private SceneHandler m_SceneHandler = new SceneHandler();

        public static SceneHandler SceneHandler => Instance.m_SceneHandler;
    }
}
