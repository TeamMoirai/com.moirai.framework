using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 默认流程处理器（纯 C# 状态机实现）。
    /// <para><see cref="ProcedureServiceHandler"/> 的内置实现，承载全部流程状态管理逻辑。</para>
    /// </summary>
    [Serializable]
    public sealed class DefaultProcedureHandler : ProcedureServiceHandler
    {
        [NonSerialized] private Dictionary<Type, ProcedureBase> _states;
        [NonSerialized] private ProcedureBase _currentState;
        [NonSerialized] private float _currentStateTime;
        [NonSerialized] private bool _isDestroyed;

        /// <summary>
        /// 当前流程。
        /// </summary>
        public override ProcedureBase CurrentProcedure
        {
            get
            {
                if (_isDestroyed)
                {
                    throw new GameException("You must initialize procedure first.");
                }

                return _currentState;
            }
        }

        /// <summary>
        /// 当前流程持续时间。
        /// </summary>
        public override float CurrentProcedureTime
        {
            get
            {
                if (_isDestroyed)
                {
                    throw new GameException("You must initialize procedure first.");
                }

                return _currentStateTime;
            }
        }

        /// <summary>
        /// 处理器初始化。
        /// </summary>
        protected override void OnInit()
        {
            _states ??= new Dictionary<Type, ProcedureBase>();
            _currentState = null;
            _currentStateTime = 0f;
            _isDestroyed = true;
        }

        /// <summary>
        /// 处理器关闭，销毁全部流程状态。
        /// </summary>
        protected override void OnShutdown()
        {
            if (!_isDestroyed)
            {
                if (_currentState != null)
                {
                    _currentState.OnLeave(true);
                }

                foreach (KeyValuePair<Type, ProcedureBase> state in _states)
                {
                    state.Value.OnDestroy();
                }

                _isDestroyed = true;
            }

            _currentState = null;
            _currentStateTime = 0f;
            _states.Clear();
        }

        /// <summary>
        /// 轮询当前流程。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (_isDestroyed || _currentState == null)
            {
                return;
            }

            _currentStateTime += elapseSeconds;
            _currentState.OnUpdate(elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 初始化流程管理器。
        /// </summary>
        /// <param name="procedures">流程管理器包含的流程。</param>
        public override void Initialize(params ProcedureBase[] procedures)
        {
            if (procedures == null || procedures.Length < 1)
            {
                throw new GameException("Procedures is invalid.");
            }

            _states.Clear();
            _currentState = null;
            _currentStateTime = 0f;
            _isDestroyed = false;

            foreach (ProcedureBase procedure in procedures)
            {
                if (procedure == null)
                {
                    throw new GameException("Procedure is invalid.");
                }

                Type procedureType = procedure.GetType();
                if (_states.ContainsKey(procedureType))
                {
                    throw new GameException(StringUtility.Format("Procedure '{0}' is already exist.", procedureType.FullName));
                }

                procedure.SetOwner(this);
                _states.Add(procedureType, procedure);
                procedure.OnInit();
            }
        }

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <param name="procedureType">要开始的流程类型。</param>
        public override void StartProcedure(Type procedureType)
        {
            if (_isDestroyed)
            {
                throw new GameException("You must initialize procedure first.");
            }

            if (_currentState != null)
            {
                throw new GameException("Procedure is running, can not start again.");
            }

            if (procedureType == null)
            {
                throw new GameException("Procedure type is invalid.");
            }

            if (!typeof(ProcedureBase).IsAssignableFrom(procedureType))
            {
                throw new GameException(StringUtility.Format("Procedure type '{0}' is invalid.", procedureType.FullName));
            }

            if (!_states.TryGetValue(procedureType, out ProcedureBase procedure))
            {
                throw new GameException(StringUtility.Format("Can not start procedure '{0}' which is not exist.", procedureType.FullName));
            }

            _currentStateTime = 0f;
            _currentState = procedure;
            _currentState.OnEnter();
        }

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <param name="procedureType">要检查的流程类型。</param>
        /// <returns>是否存在流程。</returns>
        public override bool HasProcedure(Type procedureType)
        {
            if (_isDestroyed)
            {
                throw new GameException("You must initialize procedure first.");
            }

            if (procedureType == null)
            {
                throw new GameException("Procedure type is invalid.");
            }

            if (!typeof(ProcedureBase).IsAssignableFrom(procedureType))
            {
                throw new GameException(StringUtility.Format("Procedure type '{0}' is invalid.", procedureType.FullName));
            }

            return _states.ContainsKey(procedureType);
        }

        /// <summary>
        /// 切换流程。
        /// </summary>
        /// <param name="procedureType">要切换的状态类型。</param>
        public override void ChangeState(Type procedureType)
        {
            if (_isDestroyed)
            {
                throw new GameException("You must initialize procedure first.");
            }

            if (_currentState == null)
            {
                throw new GameException("Current procedure is invalid.");
            }

            if (procedureType == null)
            {
                throw new GameException("Procedure type is invalid.");
            }

            if (!typeof(ProcedureBase).IsAssignableFrom(procedureType))
            {
                throw new GameException(StringUtility.Format("Procedure type '{0}' is invalid.", procedureType.FullName));
            }

            if (!_states.TryGetValue(procedureType, out ProcedureBase procedure))
            {
                throw new GameException(StringUtility.Format("Can not change procedure to '{0}' which is not exist.", procedureType.FullName));
            }

            _currentState.OnLeave(false);
            _currentStateTime = 0f;
            _currentState = procedure;
            _currentState.OnEnter();
        }

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <param name="procedureType">要获取的流程类型。</param>
        /// <returns>要获取的流程。</returns>
        public override ProcedureBase GetProcedure(Type procedureType)
        {
            if (_isDestroyed)
            {
                throw new GameException("You must initialize procedure first.");
            }

            if (procedureType == null)
            {
                throw new GameException("Procedure type is invalid.");
            }

            if (!typeof(ProcedureBase).IsAssignableFrom(procedureType))
            {
                throw new GameException(StringUtility.Format("Procedure type '{0}' is invalid.", procedureType.FullName));
            }

            if (_states.TryGetValue(procedureType, out ProcedureBase procedure))
            {
                return procedure;
            }

            return null;
        }

        /// <summary>
        /// 重启流程。默认使用第一个流程作为启动流程。
        /// </summary>
        /// <param name="procedures">新的流程。</param>
        /// <returns>是否重启成功。</returns>
        public override bool RestartProcedure(params ProcedureBase[] procedures)
        {
            if (procedures == null || procedures.Length <= 0)
            {
                throw new GameException("RestartProcedure Failed procedures is invalid.");
            }

            OnShutdown();
            Initialize(procedures);
            StartProcedure(procedures[0].GetType());
            return true;
        }
    }
}
