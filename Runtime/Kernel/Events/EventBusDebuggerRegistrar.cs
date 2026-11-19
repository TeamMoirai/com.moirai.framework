using Moirai.Atropos.Debugger;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 事件总线调试窗注册器：静态总线的诊断窗挂接入口。
    /// </summary>
    /// <remarks>
    /// EventBus 是内核管线静态组件（无服务生命周期）——调试窗经 [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] 注册， <br />
    /// 容器未构建时（编辑器模式/无 Debugger 服务）静默跳过，进 Play 后下次域初始化重试。
    /// </remarks>
    internal static class EventBusDebuggerRegistrar
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterDebuggerWindow()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Application.isPlaying) return;

            var debugger = GameServices.GetService<IDebuggerService>();
            if (debugger == null) return;

            debugger.RegisterDebuggerWindow("Profiler/EventBus", new EventBusDebuggerWindow());
#endif
        }
    }
}
