using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 根绑定：挂在充当 UI 根的场景物体上，告诉 UI 后端"根在这里"。
    /// <para>取代按名字查找的旧约定——名字是运行期字符串，改名不报错、只在取用时静默失效，
    /// 多场景或热更加载下还可能命中同名但不对的那一个。</para>
    /// <para>时序：<see cref="Awake"/> 完成登记；UI 后端在首个 Update tick 取用，取不到则挂起继续等
    /// （后加入的场景、运行期实例化出来的根都走这条路径）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIRootBinding : MonoBehaviour
    {
        /// <summary>当前登记的 UI 根（<c>null</c> = 还没有任何物体绑定）。</summary>
        public static UIRootBinding Current { get; private set; }

        private void Awake()
        {
            Internal_Bind();
        }

        private void OnDestroy()
        {
            Internal_Unbind();
        }

        /// <summary>
        /// 登记为当前 UI 根（<see cref="Awake"/> 调；EditMode 下 <c>AddComponent</c> 不触发 <c>Awake</c>，
        /// 故留出 internal 入口给测试与代码装配，不走反射私有方法）。
        /// </summary>
        internal void Internal_Bind()
        {
            if (Current != null && Current != this)
            {
                LogUtility.Error($"UI 根绑定重复：'{Current.name}' 已在位，'{name}' 覆盖之。一个场景只应有一个 UIRootBinding。");
            }

            Current = this;
        }

        /// <summary>让位：只有当前根是自己时才清空登记（销毁次要绑定时不得连带摘掉主根）。</summary>
        internal void Internal_Unbind()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>关闭域重载时静态位不会自动归零，这里显式清一次，避免跨会话残留已销毁的根。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCurrent()
        {
            Current = null;
        }
    }
}
