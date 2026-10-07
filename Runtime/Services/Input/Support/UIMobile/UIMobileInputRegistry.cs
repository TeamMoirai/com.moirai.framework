using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 虚拟输入组件注册表：InputButton/InputAxes 在 OnEnable/OnDisable 自注册与注销。
    /// </summary>
    /// <remarks>
    /// 延迟实例化（对象池、动态生成）的虚拟按键与摇杆始终可被 <see cref="UIMobileInputHandler"/> 查询到，销毁后不残留引用。 <br />
    /// 重名动作以后注册者为准（覆盖），并输出告警。 <br />
    /// 仅主线程。
    /// </remarks>
    public static class UIMobileInputRegistry
    {
        private static readonly Dictionary<string, InputButton> s_Buttons = new Dictionary<string, InputButton>();
        private static readonly Dictionary<string, InputAxes> s_Axes = new Dictionary<string, InputAxes>();
        private static int s_Version;

        /// <summary>当前注册的全部虚拟按钮（只读视图，元素顺序不保证）。</summary>
        public static IReadOnlyCollection<InputButton> Buttons => s_Buttons.Values;

        /// <summary>当前注册的全部虚拟摇杆（只读视图，元素顺序不保证）。</summary>
        public static IReadOnlyCollection<InputAxes> Axes => s_Axes.Values;

        /// <summary>
        /// 注册表变更版本号：每次注册（含同名覆盖）自增。
        /// </summary>
        /// <remarks>供解析方做可失效的负缓存——版本一致时未命中的动作名可直接短路，任何新注册都会令负缓存失效。</remarks>
        public static int Version => s_Version;

        /// <summary>
        /// 按动作名查询虚拟按钮；命中的组件若已被销毁则惰性清除并返回 false。
        /// </summary>
        public static bool TryGetButton(string actionName, out InputButton button)
        {
            if (!s_Buttons.TryGetValue(actionName, out button)) return false;

            if (button == null)
            {
                s_Buttons.Remove(actionName);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 按动作名查询虚拟摇杆；命中的组件若已被销毁则惰性清除并返回 false。
        /// </summary>
        public static bool TryGetAxes(string actionName, out InputAxes axes)
        {
            if (!s_Axes.TryGetValue(actionName, out axes)) return false;

            if (axes == null)
            {
                s_Axes.Remove(actionName);
                return false;
            }

            return true;
        }

        internal static void Register(InputButton button)
        {
            if (button == null) return;

            if (string.IsNullOrEmpty(button.ActionName))
            {
                LogUtility.Warning($"InputButton '{button.name}' has an empty ActionName — registration skipped.");
                return;
            }

            if (s_Buttons.TryGetValue(button.ActionName, out InputButton existing) && existing != null && !ReferenceEquals(existing, button))
            {
                LogUtility.Warning($"Duplicate InputButton action name '{button.ActionName}' — overriding previous registration.");
            }

            s_Buttons[button.ActionName] = button;
            s_Version++;
        }

        internal static void Unregister(InputButton button)
        {
            if (button == null) return;

            // 仅当映射的实例就是自己时才移除，避免旧组件的注销误删后注册的同名组件
            if (s_Buttons.TryGetValue(button.ActionName, out InputButton existing) && ReferenceEquals(existing, button))
            {
                s_Buttons.Remove(button.ActionName);
            }
        }

        internal static void Register(InputAxes axes)
        {
            if (axes == null) return;

            if (string.IsNullOrEmpty(axes.ActionName))
            {
                LogUtility.Warning($"InputAxes '{axes.name}' has an empty ActionName — registration skipped.");
                return;
            }

            if (s_Axes.TryGetValue(axes.ActionName, out InputAxes existing) && existing != null && !ReferenceEquals(existing, axes))
            {
                LogUtility.Warning($"Duplicate InputAxes action name '{axes.ActionName}' — overriding previous registration.");
            }

            s_Axes[axes.ActionName] = axes;
            s_Version++;
        }

        internal static void Unregister(InputAxes axes)
        {
            if (axes == null) return;

            if (s_Axes.TryGetValue(axes.ActionName, out InputAxes existing) && ReferenceEquals(existing, axes))
            {
                s_Axes.Remove(axes.ActionName);
            }
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStaticsForDomainReloadDisabled()
        {
            s_Buttons.Clear();
            s_Axes.Clear();
            s_Version = 0;
        }
#endif
    }
}
