using UnityEngine;

namespace Moirai.Atropos.Events
{
    // 确定事件处理程序要在哪个事件阶段处理事件，
    // 如果处理程序是目标 CallBackHandler，则始终会调用
    /// <summary>
    /// 事件的传播阶段。
    /// </summary>
    /// <remarks>
    /// 派发顺序： TrickleDown（根 → 目标父级）→ AtTarget（目标）→ DefaultActionAtTarget（目标的 at-target 默认动作）→ BubbleUp（目标父级 → 根）→ <br />
    /// DefaultAction（最终默认动作）。
    /// </remarks>
    public enum PropagationPhase
    {
        /// <summary>事件不会传播。</summary>
        /// <remarks>目前没有传播</remarks>
        None = 0,

        /// <summary>该事件从面板的根元素发送到目标元素的父元素。</summary>
        /// <remarks>从树的根传播到 target 的直接父级。</remarks>
        TrickleDown = 1,

        /// <summary>事件将发送到目标。</summary>
        /// <remarks>事件到达目标。</remarks>
        AtTarget = 2,
        
        /// <summary>事件到达目标元素后执行默认操作；此阶段处理程序不接收事件，默认操作由 ExecuteDefaultActionAtTarget 执行。</summary>
        /// <remarks>在 target 处执行默认操作。</remarks>
        DefaultActionAtTarget = 5,

        /// <summary>该事件从目标元素的父元素发送回面板的根元素。</summary>
        /// <remarks>在目标有机会处理事件后，事件会沿着父层次结构返回根。</remarks>
        BubbleUp = 3,
        
        /// <summary>事件到达 target 元素后执行最终默认操作；此阶段处理程序不接收事件，默认操作由 ExecuteDefaultAction 执行。</summary>
        /// <remarks>最后，执行默认操作。</remarks>
        DefaultAction = 4
    }
    public interface IEventDispatchingStrategy
    {
        bool CanDispatchEvent(EventBase evt);
        void DispatchEvent(EventBase evt, IEventCoordinator coordinator);
    }
    public interface IEventDispatchingListener
    {
        void OnPushDispatcherContext();
        void OnPopDispatcherContext();
    }
    internal static class EventDispatchUtilities
    {
        public static void PropagateEvent(EventBase evt)
        {
            // 如果没有目标，或者它不是 CallbackEventHandler，假设事件处理是空工作。
            if (evt.Target is not CallbackEventHandler ve)
                return;

            Debug.Assert(!evt.Dispatch, "Event is being dispatched recursively.");
            evt.Dispatch = true;

            if (!evt.BubblesOrTricklesDown)
            {
                ve.HandleEventAtTargetPhase(evt);
            }
            else
            {
                HandleEventAcrossPropagationPath(evt);
            }

            evt.Dispatch = false;
        }

        private static void HandleEventAcrossPropagationPath(EventBase evt)
        {
            // 构建和存储传播路径
            var leafTarget = (CallbackEventHandler)evt.LeafTarget;
            var path = PropagationPaths.Build(leafTarget, evt);
            evt.Path = path;
            EventDebugger.LogPropagationPaths(evt, path);

            var coordinator = leafTarget.Coordinator;

            // Phase 1: TrickleDown 阶段
            // 将事件从 root 传播到 target.parent
            if (evt.TricklesDown)
            {
                evt.PropagationPhase = PropagationPhase.TrickleDown;

                for (int i = path.TrickleDownPath.Count - 1; i >= 0; i--)
                {
                    if (evt.IsPropagationStopped)
                        break;

                    var element = path.TrickleDownPath[i];
                    if (evt.Skip(element) || element.Coordinator != coordinator)
                    {
                        continue;
                    }

                    evt.CurrentTarget = element;
                    evt.CurrentTarget.HandleEvent(evt);
                }
            }

            // Phase 2: Target / DefaultActionAtTarget
            // 将事件从目标父级传播到目标阶段的根

            // 即使传播已停止，也请调用 HandleEvent()，以执行 target 的默认操作。
            evt.PropagationPhase = PropagationPhase.AtTarget;
            foreach (var element in path.TargetElements)
            {
                if (evt.Skip(element) || element.Coordinator != coordinator)
                {
                    continue;
                }

                evt.Target = element;
                evt.CurrentTarget = evt.Target;
                evt.CurrentTarget.HandleEvent(evt);
            }

            // 调用 ExecuteDefaultActionAtTarget
            evt.PropagationPhase = PropagationPhase.DefaultActionAtTarget;
            foreach (var element in path.TargetElements)
            {
                if (evt.Skip(element) || element.Coordinator != coordinator)
                {
                    continue;
                }

                evt.Target = element;
                evt.CurrentTarget = evt.Target;
                evt.CurrentTarget.HandleEvent(evt);
            }

            // 将目标重置为原始目标
            evt.Target = evt.LeafTarget;

            // Phase 3: 气泡上升阶段
            // 将事件从目标父级传播到根
            if (evt.Bubbles)
            {
                evt.PropagationPhase = PropagationPhase.BubbleUp;

                foreach (var element in path.BubbleUpPath)
                {
                    if (evt.Skip(element) || element.Coordinator != coordinator)
                    {
                        continue;
                    }

                    evt.CurrentTarget = element;
                    evt.CurrentTarget.HandleEvent(evt);
                }
            }

            evt.PropagationPhase = PropagationPhase.None;
            evt.CurrentTarget = null;
        }


        public static void ExecuteDefaultAction(EventBase evt)
        {
            if (evt.Target is CallbackEventHandler)
            {
                evt.Dispatch = true;
                evt.CurrentTarget = evt.Target;
                evt.PropagationPhase = PropagationPhase.DefaultAction;

                evt.CurrentTarget.HandleEvent(evt);

                evt.PropagationPhase = PropagationPhase.None;
                evt.CurrentTarget = null;
                evt.Dispatch = false;
            }
        }
    }
}