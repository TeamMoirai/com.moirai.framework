using System;

namespace Moirai.Atropos.FSM
{
    /// <summary>
    /// 有限状态机状态基类。
    /// </summary>
    /// <typeparam name="T">有限状态机持有者类型。</typeparam>
    public abstract class FSMState<T> where T : class
    {
        /// <summary>
        /// 初始化有限状态机状态基类的新实例。
        /// </summary>
        public FSMState() { }

        /// <summary>
        /// 有限状态机状态初始化时调用。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        protected internal virtual void OnInit(IFSM<T> fsm)
        {
        }

        /// <summary>
        /// 有限状态机状态进入时调用。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        protected internal virtual void OnEnter(IFSM<T> fsm)
        {
        }

        /// <summary>
        /// 有限状态机状态轮询时调用。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        protected internal virtual void OnUpdate(IFSM<T> fsm, float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 有限状态机状态离开时调用。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        /// <param name="isShutdown">是否是关闭有限状态机时触发。</param>
        protected internal virtual void OnExit(IFSM<T> fsm, bool isShutdown)
        {
        }

        /// <summary>
        /// 有限状态机状态销毁时调用。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        protected internal virtual void OnDestroy(IFSM<T> fsm)
        {
        }

        /// <summary>
        /// 切换当前有限状态机状态。
        /// </summary>
        /// <typeparam name="TState">要切换到的有限状态机状态类型。</typeparam>
        /// <param name="fsm">有限状态机引用。</param>
        protected void ChangeState<TState>(IFSM<T> fsm) where TState : FSMState<T>
        {
            FSM<T> fsmImplement = (FSM<T>)fsm;
            if (fsmImplement == null)
            {
                throw new GameException("FSM is invalid.");
            }

            fsmImplement.ChangeState<TState>();
        }

        /// <summary>
        /// 切换当前有限状态机状态。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        /// <param name="stateType">要切换到的有限状态机状态类型。</param>
        protected void ChangeState(IFSM<T> fsm, Type stateType)
        {
            FSM<T> fsmImplement = (FSM<T>)fsm;
            if (fsmImplement == null)
            {
                throw new GameException("FSM is invalid.");
            }

            if (stateType == null)
            {
                throw new GameException("State type is invalid.");
            }

            if (!typeof(FSMState<T>).IsAssignableFrom(stateType))
            {
                throw new GameException(StringUtility.Format("State type '{0}' is invalid.", stateType.FullName));
            }

            fsmImplement.ChangeState(stateType);
        }
    }
}
