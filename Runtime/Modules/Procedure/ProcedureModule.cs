using System;
using Moirai.Atropos.FSM;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程管理模块。
    /// </summary>
    public sealed class ProcedureModule : Module, IProcedureModule
    {
        private IFSMModule _fsmModule;
        private IFSM<IProcedureModule> _procedureFsm;

        public override int Priority => -2;
        
        public ProcedureBase CurrentProcedure
        {
            get
            {
                if (_procedureFsm == null)
                {
                    throw new GameException("You must initialize procedure first.");
                }

                return (ProcedureBase)_procedureFsm.CurrentState;
            }
        }
        
        public float CurrentProcedureTime
        {
            get
            {
                if (_procedureFsm == null)
                {
                    throw new GameException("You must initialize procedure first.");
                }

                return _procedureFsm.CurrentStateTime;
            }
        }

        public override void OnInit()
        {
            _fsmModule = null;
            _procedureFsm = null;
        }
        
        public override void Shutdown()
        {
            if (_fsmModule != null)
            {
                if (_procedureFsm != null)
                {
                    _fsmModule.DestroyFSM(_procedureFsm);
                    _procedureFsm = null;
                }

                _fsmModule = null;
            }
        }
        
        public void Initialize(IFSMModule fsmModule, params ProcedureBase[] procedures)
        {
            if (fsmModule == null)
            {
                throw new GameException("FSM manager is invalid.");
            }

            _fsmModule = fsmModule;
            _procedureFsm = _fsmModule.CreateFSM(this, procedures);
        }
        
        public void StartProcedure<T>() where T : ProcedureBase
        {
            if (_procedureFsm == null)
            {
                throw new GameException("You must initialize procedure first.");
            }

            _procedureFsm.Start<T>();
        }
        
        public void StartProcedure(Type procedureType)
        {
            if (_procedureFsm == null)
            {
                throw new GameException("You must initialize procedure first.");
            }

            _procedureFsm.Start(procedureType);
        }
        
        public bool HasProcedure<T>() where T : ProcedureBase
        {
            if (_procedureFsm == null)
            {
                throw new GameException("You must initialize procedure first.");
            }

            return _procedureFsm.HasState<T>();
        }
        
        public bool HasProcedure(Type procedureType)
        {
            if (_procedureFsm == null)
            {
                throw new GameException("You must initialize procedure first.");
            }

            return _procedureFsm.HasState(procedureType);
        }
        
        public void ChangeState<T>() where T : ProcedureBase
        {
            if (_procedureFsm == null)
            {
                throw new GameException("You must initialize procedure first.");
            }

            _procedureFsm.ChangeState<T>();
        }

        public void ChangeState(Type procedureType)
        {
            if (_procedureFsm == null)
            {
                throw new GameException("You must initialize procedure first.");
            }

            _procedureFsm.ChangeState(procedureType);
        }
        
        public ProcedureBase GetProcedure<T>() where T : ProcedureBase
        {
            if (_procedureFsm == null)
            {
                throw new GameException("You must initialize procedure first.");
            }

            return _procedureFsm.GetState<T>();
        }
        
        public ProcedureBase GetProcedure(Type procedureType)
        {
            if (_procedureFsm == null)
            {
                throw new GameException("You must initialize procedure first.");
            }

            return (ProcedureBase)_procedureFsm.GetState(procedureType);
        }
        
        public bool RestartProcedure(params ProcedureBase[] procedures)
        {
            if (procedures == null || procedures.Length <= 0)
            {
                throw new GameException("RestartProcedure Failed procedures is invalid.");
            }

            if (!_fsmModule.DestroyFSM<IProcedureModule>())
            {
                return false;
            }

            Initialize(_fsmModule, procedures);
            StartProcedure(procedures[0].GetType());
            return true;
        }
    }
}