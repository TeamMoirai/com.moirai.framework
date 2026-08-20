using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程管理服务 — 自包含状态机，不依赖外部 FSM 服务。
    /// </summary>
    public sealed class ProcedureService : ServiceBase, IProcedureService, IServiceTickable
    {
        private Dictionary<Type, ProcedureBase> _states;
        private ProcedureBase _currentState;
        private float _currentStateTime;
        private bool _isDestroyed;

        public override int Priority => -2;

        /// <summary>
        /// 无参构造 — 状态管理完全自包含。
        /// </summary>
        public ProcedureService()
        {
            _states = new Dictionary<Type, ProcedureBase>();
            _currentState = null;
            _currentStateTime = 0f;
            _isDestroyed = true;
        }

        public ProcedureBase CurrentProcedure
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

        public float CurrentProcedureTime
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

        public override void OnInit()
        {
            _states ??= new Dictionary<Type, ProcedureBase>();
            _currentState = null;
            _currentStateTime = 0f;
            _isDestroyed = true;
        }

        public override void Shutdown()
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
        }

        public void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (_isDestroyed || _currentState == null)
            {
                return;
            }

            _currentStateTime += elapseSeconds;
            _currentState.OnUpdate(elapseSeconds, realElapseSeconds);
        }

        public void Initialize(params ProcedureBase[] procedures)
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

        public void StartProcedure<T>() where T : ProcedureBase
        {
            StartProcedure(typeof(T));
        }

        public void StartProcedure(Type procedureType)
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

        public bool HasProcedure<T>() where T : ProcedureBase
        {
            return HasProcedure(typeof(T));
        }

        public bool HasProcedure(Type procedureType)
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

        public void ChangeState<T>() where T : ProcedureBase
        {
            ChangeState(typeof(T));
        }

        public void ChangeState(Type procedureType)
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

        public ProcedureBase GetProcedure<T>() where T : ProcedureBase
        {
            return GetProcedure(typeof(T));
        }

        public ProcedureBase GetProcedure(Type procedureType)
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

        public bool RestartProcedure(params ProcedureBase[] procedures)
        {
            if (procedures == null || procedures.Length <= 0)
            {
                throw new GameException("RestartProcedure Failed procedures is invalid.");
            }

            Shutdown();
            Initialize(procedures);
            StartProcedure(procedures[0].GetType());
            return true;
        }
    }
}
