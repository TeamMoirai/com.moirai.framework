using System;
using System.Collections.Generic;

namespace Moirai.Atropos.FSM
{
    /// <summary>
    /// 有限状态机模块。
    /// </summary>
    public sealed class FSMModule : Module, IFSMModule, IUpdateModule
    {
        private Dictionary<TypeNamePair, FSMBase> _fsmMap;
        private List<FSMBase> _tempFSMList;

        public override int Priority => 1;

        public int Count => _fsmMap.Count;

        public override void OnInit()
        {
            _fsmMap = new Dictionary<TypeNamePair, FSMBase>();
            _tempFSMList = new List<FSMBase>();
        }
        
        public override void Shutdown()
        {
            foreach (KeyValuePair<TypeNamePair, FSMBase> fsm in _fsmMap)
            {
                fsm.Value.Shutdown();
            }

            _fsmMap.Clear();
            _tempFSMList.Clear();
        }
        
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            _tempFSMList.Clear();
            if (_fsmMap.Count <= 0)
            {
                return;
            }

            foreach (KeyValuePair<TypeNamePair, FSMBase> fsm in _fsmMap)
            {
                _tempFSMList.Add(fsm.Value);
            }

            foreach (FSMBase fsm in _tempFSMList)
            {
                if (fsm.IsDestroyed)
                {
                    continue;
                }

                fsm.Update(elapseSeconds, realElapseSeconds);
            }
        }
        
        public bool HasFSM<T>() where T : class
        {
            return InternalHasFSM(new TypeNamePair(typeof(T)));
        }
        
        public bool HasFSM(Type ownerType)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalHasFSM(new TypeNamePair(ownerType));
        }
        
        public bool HasFSM<T>(string name) where T : class
        {
            return InternalHasFSM(new TypeNamePair(typeof(T), name));
        }
        
        public bool HasFSM(Type ownerType, string name)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalHasFSM(new TypeNamePair(ownerType, name));
        }
        
        public IFSM<T> GetFSM<T>() where T : class
        {
            return (IFSM<T>)InternalGetFSM(new TypeNamePair(typeof(T)));
        }
        
        public FSMBase GetFSM(Type ownerType)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalGetFSM(new TypeNamePair(ownerType));
        }
        
        public IFSM<T> GetFSM<T>(string name) where T : class
        {
            return (IFSM<T>)InternalGetFSM(new TypeNamePair(typeof(T), name));
        }
        
        public FSMBase GetFSM(Type ownerType, string name)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalGetFSM(new TypeNamePair(ownerType, name));
        }
        
        public FSMBase[] GetAllFSMs()
        {
            int index = 0;
            FSMBase[] results = new FSMBase[_fsmMap.Count];
            foreach (KeyValuePair<TypeNamePair, FSMBase> fsm in _fsmMap)
            {
                results[index++] = fsm.Value;
            }

            return results;
        }
        
        public void GetAllFSMs(List<FSMBase> results)
        {
            if (results == null)
            {
                throw new GameException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<TypeNamePair, FSMBase> fsm in _fsmMap)
            {
                results.Add(fsm.Value);
            }
        }
        
        public IFSM<T> CreateFSM<T>(T owner, params FSMState<T>[] states) where T : class
        {
            return CreateFSM(string.Empty, owner, states);
        }
        
        public IFSM<T> CreateFSM<T>(string name, T owner, params FSMState<T>[] states) where T : class
        {
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (HasFSM<T>(name))
            {
                throw new GameException(StringUtility.Format("Already exist FSM '{0}'.", typeNamePair));
            }

            FSM<T> fsm = FSM<T>.Create(name, owner, states);
            _fsmMap.Add(typeNamePair, fsm);
            return fsm;
        }
        
        public IFSM<T> CreateFSM<T>(T owner, List<FSMState<T>> states) where T : class
        {
            return CreateFSM(string.Empty, owner, states);
        }
        
        public IFSM<T> CreateFSM<T>(string name, T owner, List<FSMState<T>> states) where T : class
        {
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (HasFSM<T>(name))
            {
                throw new GameException(StringUtility.Format("Already exist FSM '{0}'.", typeNamePair));
            }

            FSM<T> fsm = FSM<T>.Create(name, owner, states);
            _fsmMap.Add(typeNamePair, fsm);
            return fsm;
        }
        
        public bool DestroyFSM<T>() where T : class
        {
            return InternalDestroyFSM(new TypeNamePair(typeof(T)));
        }
        
        public bool DestroyFSM(Type ownerType)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalDestroyFSM(new TypeNamePair(ownerType));
        }
        
        public bool DestroyFSM<T>(string name) where T : class
        {
            return InternalDestroyFSM(new TypeNamePair(typeof(T), name));
        }
        
        public bool DestroyFSM(Type ownerType, string name)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalDestroyFSM(new TypeNamePair(ownerType, name));
        }
        
        public bool DestroyFSM<T>(IFSM<T> fsm) where T : class
        {
            if (fsm == null)
            {
                throw new GameException("FSM is invalid.");
            }

            return InternalDestroyFSM(new TypeNamePair(typeof(T), fsm.Name));
        }
        
        public bool DestroyFSM(FSMBase fsm)
        {
            if (fsm == null)
            {
                throw new GameException("FSM is invalid.");
            }

            return InternalDestroyFSM(new TypeNamePair(fsm.OwnerType, fsm.Name));
        }
        
        private bool InternalHasFSM(TypeNamePair typeNamePair)
        {
            return _fsmMap.ContainsKey(typeNamePair);
        }
        
        private FSMBase InternalGetFSM(TypeNamePair typeNamePair)
        {
            FSMBase fsm = null;
            if (_fsmMap.TryGetValue(typeNamePair, out fsm))
            {
                return fsm;
            }

            return null;
        }
        
        private bool InternalDestroyFSM(TypeNamePair typeNamePair)
        {
            FSMBase fsm = null;
            if (_fsmMap.TryGetValue(typeNamePair, out fsm))
            {
                fsm.Shutdown();
                return _fsmMap.Remove(typeNamePair);
            }

            return false;
        }
    }
}
