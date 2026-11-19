using System;
using System.Collections;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 帧与 Unity 事件订阅：帧逻辑走 PlayerLoopDriver 静态表，订阅句柄 Subscription 定义于此。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="GameApp"/>——公共 API 零变化（验收：单一职责 < 400 行）。</remarks>
    public partial class GameApp
    {

        /// <summary>
        /// <see cref="GameApp"/> 各类订阅的可注销句柄。
        /// </summary>
        /// <remarks>
        /// 注册表按委托相等比较注销，lambda 每次求值都是新实例、摘不掉；句柄在注册时攥住确切实例，lambda 也能干净注销。 <br />
        /// 非线程安全，只在主线程创建与释放；框架 <see cref="Shutdown"/> 后 Dispose 是空操作。
        /// </remarks>
        public sealed class Subscription : IDisposable
        {
            private Action _disposeAction;

            internal Subscription(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            /// <summary>是否仍处于订阅状态（未 Dispose 过）。</summary>
            public bool IsSubscribed => _disposeAction != null;

            /// <summary>
            /// 注销订阅。幂等——重复调用安全。
            /// </summary>
            public void Dispose()
            {
                Action dispose = _disposeAction;
                if (dispose == null) return;

                _disposeAction = null;
                dispose();
            }
        }


        /// <summary>
        /// 添加帧更新事件。订阅写入 <see cref="PlayerLoopDriver"/> 静态表，宿主销毁不丢失。
        /// </summary>
        /// <param name="action">帧回调；<c>null</c> 时不注册并返回 <c>null</c>。</param>
        /// <returns>可用于注销的句柄（同参数重复注册会被驱动去重，任一持有句柄 Dispose 即注销该唯一登记）。
        /// 用 lambda 注册时<b>只能</b>靠本句柄注销——<see cref="RemoveUpdateListener"/> 按委托相等比较，
        /// 再写一个同样体的 lambda 是新实例，摘不掉。</returns>
        public static Subscription AddUpdateListener(Action action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddUpdateCallback(action);
            return new Subscription(() => PlayerLoopDriver.RemoveUpdateCallback(action));
        }

        /// <summary>
        /// 添加物理帧更新事件。语义同 <see cref="AddUpdateListener"/>。
        /// </summary>
        public static Subscription AddFixedUpdateListener(Action action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddFixedUpdateCallback(action);
            return new Subscription(() => PlayerLoopDriver.RemoveFixedUpdateCallback(action));
        }

        /// <summary>
        /// 添加Late帧更新事件。语义同 <see cref="AddUpdateListener"/>。
        /// </summary>
        public static Subscription AddLateUpdateListener(Action action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddLateUpdateCallback(action);
            return new Subscription(() => PlayerLoopDriver.RemoveLateUpdateCallback(action));
        }

        /// <summary>
        /// 移除帧更新事件。仅对能取回同一委托实例的注册有效（方法组、缓存于字段的委托）。
        /// </summary>
        public static void RemoveUpdateListener(Action action)
        {
            PlayerLoopDriver.RemoveUpdateCallback(action);
        }

        /// <summary>
        /// 移除物理帧更新事件。有效性同 <see cref="RemoveUpdateListener"/>。
        /// </summary>
        public static void RemoveFixedUpdateListener(Action action)
        {
            PlayerLoopDriver.RemoveFixedUpdateCallback(action);
        }

        /// <summary>
        /// 移除Late帧更新事件。有效性同 <see cref="RemoveUpdateListener"/>。
        /// </summary>
        public static void RemoveLateUpdateListener(Action action)
        {
            PlayerLoopDriver.RemoveLateUpdateCallback(action);
        }


        /// <summary>
        /// 订阅帧逻辑到 Update 阶段（接口式）。
        /// </summary>
        /// <remarks>
        /// Action 适合无状态挂钩，Handler 适合携带状态、需要阶段内顺序的系统（实现 <see cref="IPlayerLoopPriority"/> 控制先后，数值小者先跑）。 <br />
        /// 同一实例重复注册被忽略，驱动中调用延迟到本阶段结束后提交；注册表持强引用，注销必须成对，热路径禁止堆分配。
        /// </remarks>
        public static void AddUpdateHandler(IUpdateHandler handler)
        {
            PlayerLoopDriver.Register(handler);
        }

        /// <summary>
        /// 反注册 Update 阶段 Handler。
        /// </summary>
        public static void RemoveUpdateHandler(IUpdateHandler handler)
        {
            PlayerLoopDriver.Unregister(handler);
        }

        /// <summary>
        /// 订阅帧逻辑到 FixedUpdate 阶段。语义同 <see cref="AddUpdateHandler"/>。
        /// </summary>
        public static void AddFixedUpdateHandler(IFixedUpdateHandler handler)
        {
            PlayerLoopDriver.Register(handler);
        }

        /// <summary>
        /// 反注册 FixedUpdate 阶段 Handler。
        /// </summary>
        public static void RemoveFixedUpdateHandler(IFixedUpdateHandler handler)
        {
            PlayerLoopDriver.Unregister(handler);
        }

        /// <summary>
        /// 订阅帧逻辑到 LateUpdate 阶段（晚于 <c>MonoBehaviour.LateUpdate</c>）。
        /// </summary>
        public static void AddLateUpdateHandler(ILateUpdateHandler handler)
        {
            PlayerLoopDriver.Register(handler);
        }

        /// <summary>
        /// 反注册 LateUpdate 阶段 Handler。
        /// </summary>
        public static void RemoveLateUpdateHandler(ILateUpdateHandler handler)
        {
            PlayerLoopDriver.Unregister(handler);
        }

        /// <summary>
        /// 把对象注册到它实现的每一个帧阶段（Update / FixedUpdate / LateUpdate）。
        /// </summary>
        public static void AddFrameHandler(object handler)
        {
            PlayerLoopDriver.RegisterAll(handler);
        }

        /// <summary>
        /// 从其曾注册的全部帧阶段注销。语义同 <see cref="AddFrameHandler"/> 的逆。
        /// </summary>
        public static void RemoveFrameHandler(object handler)
        {
            PlayerLoopDriver.UnregisterAll(handler);
        }
    }
}
