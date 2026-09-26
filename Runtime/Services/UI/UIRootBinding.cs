using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 根绑定：挂在充当 UI 根的场景物体上，告诉 UI 后端"根在这里"。
    /// <para>取代按名字查找的旧约定——名字是运行期字符串，改名不报错、只在取用时静默失效，
    /// 多场景或热更加载下还可能命中同名但不对的那一个。</para>
    /// <para>单例语义（<see cref="SingletonMono{T}"/>）：先到先得，后到的整物体销毁；
    /// <see cref="SingletonMono{T}.TryGetInstance()"/> 取值不自动创建——场景里没有根就是没有，由 UI 后端续等。</para>
    /// <para>时序：基类 <c>Awake</c> 完成登记；UI 后端在首个 Update tick 取用，取不到则挂起继续等
    /// （后加入的场景、运行期实例化出来的根都走这条路径）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIRootBinding : SingletonMono<UIRootBinding>
    {
        /// <summary>
        /// 登记为当前 UI 根（基类 <c>Awake</c> 已调 <see cref="SingletonMono{T}.CheckMultipleInstance"/>；
        /// EditMode 下 <c>AddComponent</c> 不触发 <c>Awake</c>，故留出 internal 入口给测试与代码装配）。
        /// </summary>
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
