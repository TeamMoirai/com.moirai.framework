using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 泛型单例的静态状态重置注册中心。
    /// </summary>
    /// <remarks>
    /// Unity 不允许 <c>[RuntimeInitializeOnLoadMethod]</c> 挂在泛型类上，也无法枚举全部封闭泛型，
    /// 因此把重置入口提升到此非泛型类：各泛型单例在静态构造时自报回调，进 Play 时统一执行。 <br />
    /// 注册表本身不清理——关闭 Domain Reload 时回调需要跨播放会话保留。
    /// </remarks>
    internal static class SingletonStaticStateRegistry
    {
        private static readonly object s_Locker = new object();
        private static readonly List<Action> s_Resetters = new List<Action>(8);

        /// <summary>泛型类型静态构造时自报重置回调（每个封闭泛型类型一份）。</summary>
        internal static void Register(Action resetter)
        {
            lock (s_Locker)
            {
                s_Resetters.Add(resetter);
            }
        }

        /// <summary>进 Play 时清空全部已物化泛型单例的会话残留静态状态。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAll()
        {
            Action[] snapshot;
            lock (s_Locker)
            {
                snapshot = s_Resetters.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i]();
            }
        }
    }
}
