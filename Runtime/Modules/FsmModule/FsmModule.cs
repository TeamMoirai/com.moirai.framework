using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Fsm
{
    /// <summary>
    /// 有限状态机模块。
    /// </summary>
    public sealed class FsmModule : Module, IFsmModule, IUpdateModule
    {
        private Dictionary<TypeNamePair, FsmBase> _fsmMap;
        private List<FsmBase> _tempFsmList;

        public override int Priority => 1;

        public int Count => _fsmMap.Count;

        public override void OnInit()
        {
            _fsmMap = new Dictionary<TypeNamePair, FsmBase>();
            _tempFsmList = new List<FsmBase>();
        }
        
        public override void Shutdown()
        {
            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in _fsmMap)
            {
                fsm.Value.Shutdown();
            }

            _fsmMap.Clear();
            _tempFsmList.Clear();
        }
        
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            _tempFsmList.Clear();
            if (_fsmMap.Count <= 0)
            {
                return;
            }

            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in _fsmMap)
            {
                _tempFsmList.Add(fsm.Value);
            }

            foreach (FsmBase fsm in _tempFsmList)
            {
                if (fsm.IsDestroyed)
                {
                    continue;
                }

                fsm.Update(elapseSeconds, realElapseSeconds);
            }
        }
        
        public bool HasFsm<T>() where T : class
        {
            return InternalHasFsm(new TypeNamePair(typeof(T)));
        }
        
        public bool HasFsm(Type ownerType)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalHasFsm(new TypeNamePair(ownerType));
        }
        
        public bool HasFsm<T>(string name) where T : class
        {
            return InternalHasFsm(new TypeNamePair(typeof(T), name));
        }
        
        public bool HasFsm(Type ownerType, string name)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalHasFsm(new TypeNamePair(ownerType, name));
        }
        
        public IFsm<T> GetFsm<T>() where T : class
        {
            return (IFsm<T>)InternalGetFsm(new TypeNamePair(typeof(T)));
        }
        
        public FsmBase GetFsm(Type ownerType)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalGetFsm(new TypeNamePair(ownerType));
        }
        
        public IFsm<T> GetFsm<T>(string name) where T : class
        {
            return (IFsm<T>)InternalGetFsm(new TypeNamePair(typeof(T), name));
        }
        
        public FsmBase GetFsm(Type ownerType, string name)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalGetFsm(new TypeNamePair(ownerType, name));
        }
        
        public FsmBase[] GetAllFsms()
        {
            int index = 0;
            FsmBase[] results = new FsmBase[_fsmMap.Count];
            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in _fsmMap)
            {
                results[index++] = fsm.Value;
            }

            return results;
        }
        
        public void GetAllFsms(List<FsmBase> results)
        {
            if (results == null)
            {
                throw new GameException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in _fsmMap)
            {
                results.Add(fsm.Value);
            }
        }
        
        public IFsm<T> CreateFsm<T>(T owner, params FsmState<T>[] states) where T : class
        {
            return CreateFsm(string.Empty, owner, states);
        }
        
        public IFsm<T> CreateFsm<T>(string name, T owner, params FsmState<T>[] states) where T : class
        {
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (HasFsm<T>(name))
            {
                throw new GameException(StringUtility.Format("Already exist FSM '{0}'.", typeNamePair));
            }

            Fsm<T> fsm = Fsm<T>.Create(name, owner, states);
            _fsmMap.Add(typeNamePair, fsm);
            return fsm;
        }
        
        public IFsm<T> CreateFsm<T>(T owner, List<FsmState<T>> states) where T : class
        {
            return CreateFsm(string.Empty, owner, states);
        }
        
        public IFsm<T> CreateFsm<T>(string name, T owner, List<FsmState<T>> states) where T : class
        {
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (HasFsm<T>(name))
            {
                throw new GameException(StringUtility.Format("Already exist FSM '{0}'.", typeNamePair));
            }

            Fsm<T> fsm = Fsm<T>.Create(name, owner, states);
            _fsmMap.Add(typeNamePair, fsm);
            return fsm;
        }
        
        public bool DestroyFsm<T>() where T : class
        {
            return InternalDestroyFsm(new TypeNamePair(typeof(T)));
        }
        
        public bool DestroyFsm(Type ownerType)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(ownerType));
        }
        
        public bool DestroyFsm<T>(string name) where T : class
        {
            return InternalDestroyFsm(new TypeNamePair(typeof(T), name));
        }
        
        public bool DestroyFsm(Type ownerType, string name)
        {
            if (ownerType == null)
            {
                throw new GameException("Owner type is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(ownerType, name));
        }
        
        public bool DestroyFsm<T>(IFsm<T> fsm) where T : class
        {
            if (fsm == null)
            {
                throw new GameException("FSM is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(typeof(T), fsm.Name));
        }
        
        public bool DestroyFsm(FsmBase fsm)
        {
            if (fsm == null)
            {
                throw new GameException("FSM is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(fsm.OwnerType, fsm.Name));
        }
        
        private bool InternalHasFsm(TypeNamePair typeNamePair)
        {
            return _fsmMap.ContainsKey(typeNamePair);
        }
        
        private FsmBase InternalGetFsm(TypeNamePair typeNamePair)
        {
            FsmBase fsm = null;
            if (_fsmMap.TryGetValue(typeNamePair, out fsm))
            {
                return fsm;
            }

            return null;
        }
        
        private bool InternalDestroyFsm(TypeNamePair typeNamePair)
        {
            FsmBase fsm = null;
            if (_fsmMap.TryGetValue(typeNamePair, out fsm))
            {
                fsm.Shutdown();
                return _fsmMap.Remove(typeNamePair);
            }

            return false;
        }
    }
}
