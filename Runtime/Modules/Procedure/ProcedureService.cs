using System;
using Moirai.Atropos.FSM;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程管理服务。
    /// </summary>
    public sealed class ProcedureService : ServiceBase, IProcedureService
    {
        private IFSMService _fsmService;
        private IFSM<IProcedureService> _procedureFsm;

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
            _fsmService = null;
            _procedureFsm = null;
        }
        
        public override void Shutdown()
        {
            if (_fsmService != null)
            {
                if (_procedureFsm != null)
                {
                    _fsmService.DestroyFSM(_procedureFsm);
                    _procedureFsm = null;
                }

                _fsmService = null;
            }
        }
        
        public void Initialize(IFSMService fsmService, params ProcedureBase[] procedures)
        {
            if (fsmService == null)
            {
                throw new GameException("FSM manager is invalid.");
            }

            _fsmService = fsmService;
            _procedureFsm = _fsmService.CreateFSM(this, procedures);
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

            if (!_fsmService.DestroyFSM<IProcedureService>())
            {
                return false;
            }

            Initialize(_fsmService, procedures);
            StartProcedure(procedures[0].GetType());
            return true;
        }
    }
}