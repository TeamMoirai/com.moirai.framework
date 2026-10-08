using UnityEngine;

namespace Moirai.Atropos.UI.Adapter
{
    /// <summary>
    /// UI 适配器抽象基类。
    /// </summary>
    /// <remarks>
    /// 要求挂载对象具有 <see cref="RectTransform"/>，且同一对象上不可重复挂载。
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public abstract class AdapterBase : MonoBehaviour
    {
        /// <summary>
        /// 执行 UI 适配，将布局调整至目标尺寸或状态。
        /// </summary>
        public abstract void Adapt();
    }
}