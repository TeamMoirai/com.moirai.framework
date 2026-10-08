using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 根绑定：挂在充当 UI 根的场景物体上，告诉 UI 后端"根在这里"。
    /// </summary>
    /// <remarks>
    /// 单例语义（<see cref="SingletonMono{T}"/>）：先到先得，后到的整物体销毁。<br />
    /// <see cref="SingletonMono{T}.TryGetInstance()"/> 只取值，不自动创建。<br />
    /// 时序：基类 <c>Awake</c> 完成登记；UI 后端在首个 Update tick 取用，取不到则挂起续等。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UIRootBinding : SingletonMono<UIRootBinding>
    {
        /// <summary>
        /// 登记为当前 UI 根。
        /// </summary>
        /// <remarks>
        /// 供测试与代码装配使用：EditMode 下 <c>AddComponent</c> 不触发 <c>Awake</c>，无法由基类完成登记。
        /// </remarks>
        internal void Internal_Bind()
        {
            CheckMultipleInstance();
            if (s_Instance == this)
            {
                // 对齐基类 Awake：EditMode 下 OnDestroy 置 s_ShuttingDown 后不复位，不清则 TryGetInstance 恒空
                s_ShuttingDown = false;
            }
        }
    }
}
