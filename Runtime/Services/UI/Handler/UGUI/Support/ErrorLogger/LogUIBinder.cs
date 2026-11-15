using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 错误日志窗的序列化引用 binder：Text/Button 直接序列化引用，替代按名查找。
    /// </summary>
    /// <remarks>
    /// 手写形态沿 launcher binder 先例（见 docs/superpowers/plans/2026-10-11-launcher-ui-alignment.md）；
    /// 节点已按生成器语法命名（Text@LogError / Btn@LogClose），接上编辑器产线后可换 UI 生成器产出。
    /// </remarks>
    public sealed class LogUIBinder : UIBindComponent
    {
        [SerializeField] private Text m_TextLogError;
        public Text TextLogError => m_TextLogError;

        [SerializeField] private Button m_BtnLogClose;
        public Button BtnLogClose => m_BtnLogClose;
    }
}
