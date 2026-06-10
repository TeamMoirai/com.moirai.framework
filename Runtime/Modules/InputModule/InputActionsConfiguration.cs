using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 此配置便于按功能分为更小的簇，比如 Player、UI，用于剥离引用。
    /// </summary>
    /// <remarks>与 InputSystem 的 Generate class 功能类似</remarks>
    [CreateAssetMenu(menuName = "Moirai Framework/Input/InputActions Config")]
    public class InputActionsConfiguration : ScriptableObject
    {
        // Generate C# Class
        [Header("脚本生成 [Generate C# Class]")]
        [SerializeField] internal string m_ClassName = "InputActions";
        [SerializeField] internal string m_Namespace = "Moirai.Gameplay";
        
        // Actions Config
        [Title("按键配置 [Actions Config]", "输入处理器的所有按键配置，注意名称一一对应。")]
        [Tooltip("按键组，用于精确索引下述按键配置（置空为全局）")]
        [SerializeField] internal string m_ActionsGroup = string.Empty;

        // 存储布尔类型动作的名称数组
        [SerializeField] internal string[] m_BoolActions;
        // 存储浮点类型动作的名称数组
        [SerializeField] internal string[] m_FloatActions;
        // 存储二维向量类型动作的名称数组
        [SerializeField] internal string[] m_Vector2Actions;
    }
}