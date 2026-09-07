using System;
using System.Collections.Generic;
using Moirai.Atropos.Pool;

namespace Moirai.Atropos.Events
{
    internal class PropagationPaths
    {
        private static readonly _ObjectPool<PropagationPaths> s_Pool = new _ObjectPool<PropagationPaths>(() => new PropagationPaths());

        /// <summary>
        /// 传播路径包含的阶段标记。
        /// </summary>
        [Flags]
        public enum Type
        {
            /// <summary>
            /// 无任何传播路径。
            /// </summary>
            None = 0,
            /// <summary>
            /// 包含 TrickleDown（下探）路径。
            /// </summary>
            TrickleDown = 1,
            /// <summary>
            /// 包含 BubbleUp（冒泡）路径。
            /// </summary>
            BubbleUp = 2
        }

        /// <summary>
        /// TrickleDown（下探）阶段的处理元素列表。
        /// </summary>
        public readonly List<CallbackEventHandler> TrickleDownPath;

        /// <summary>
        /// 事件目标元素列表。
        /// </summary>
        public readonly List<CallbackEventHandler> TargetElements;

        /// <summary>
        /// BubbleUp（冒泡）阶段的处理元素列表。
        /// </summary>
        public readonly List<CallbackEventHandler> BubbleUpPath;

        private const int k_DefaultPropagationDepth = 16;
        
        private const int k_DefaultTargetCount = 4;

        /// <summary>
        /// 初始化空的传播路径实例。
        /// </summary>
        public PropagationPaths()
        {
            TrickleDownPath = new List<CallbackEventHandler>(k_DefaultPropagationDepth);
            TargetElements = new List<CallbackEventHandler>(k_DefaultTargetCount);
            BubbleUpPath = new List<CallbackEventHandler>(k_DefaultPropagationDepth);
        }

        /// <summary>
        /// 以现有传播路径的副本初始化传播路径实例。
        /// </summary>
        /// <param name="paths">作为数据来源的传播路径。</param>
        public PropagationPaths(PropagationPaths paths)
        {
            TrickleDownPath = new List<CallbackEventHandler>(paths.TrickleDownPath);
            TargetElements = new List<CallbackEventHandler>(paths.TargetElements);
            BubbleUpPath = new List<CallbackEventHandler>(paths.BubbleUpPath);
        }

        internal static PropagationPaths Copy(PropagationPaths paths)
        {
            PropagationPaths copyPaths = s_Pool.Get();
            copyPaths.TrickleDownPath.AddRange(paths.TrickleDownPath);
            copyPaths.TargetElements.AddRange(paths.TargetElements);
            copyPaths.BubbleUpPath.AddRange(paths.BubbleUpPath);

            return copyPaths;
        }

        /// <summary>
        /// 从指定元素沿层级向上构建事件的传播路径。
        /// </summary>
        /// <param name="elem">事件的起始目标元素。</param>
        /// <param name="evt">要传播的事件。</param>
        /// <returns>构建好的传播路径（取自对象池）。</returns>
        public static PropagationPaths Build(CallbackEventHandler elem, EventBase evt)
        {
            PropagationPaths paths = s_Pool.Get();
            // 遍历整个层级。
            for (var ve = elem.Parent; ve != null; ve = ve.Parent)
            {
                // 到达根节点
                if (ve.IsCompositeRoot && !evt.SkipDisabledElements)
                {
                    paths.TargetElements.Add(ve);
                }
                else
                {
                    if (evt.TricklesDown && ve.HasTrickleDownHandlers())
                    {
                        paths.TrickleDownPath.Add(ve);
                    }
                    if (evt.Bubbles && ve.HasBubbleUpHandlers())
                    {
                        paths.BubbleUpPath.Add(ve);
                    }
                }
            }
            return paths;
        }

        /// <summary>
        /// 清空所有路径并将实例归还对象池。
        /// </summary>
        public void Release()
        {
            // 清空路径以避免 CallbackEventHandler 泄漏。
            BubbleUpPath.Clear();
            TargetElements.Clear();
            TrickleDownPath.Clear();

            s_Pool.Release(this);
        }
    }
}
