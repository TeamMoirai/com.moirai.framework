using System;
using System.Collections;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Internal;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.UpdateDriver
{
    /// <summary>
    /// 更新驱动处理器。通过常驻 GameObject（MainBehaviour）承载协程与 Unity 帧事件注入。
    /// <para>可在 <see cref="UpdateDriverSettings"/> 中替换为自定义实现。</para>
    /// </summary>
    [Serializable]
    public class UpdateDriverHandler : FrameworkHandler
    {
        private GameObject _entity;
        private MainBehaviour _behaviour;

        protected override void OnInit()
        {
            _MakeEntity();
        }

        protected override void OnShutdown()
        {
            if (_behaviour != null)
            {
                _behaviour.Release();
            }

            if (_entity != null)
            {
                Object.Destroy(_entity);
            }

            _entity = null;
        }

        #region 控制协程 [COROUTINE CONTROL]

        public Coroutine StartCoroutine(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            _MakeEntity();
            return _behaviour.StartCoroutine(methodName);
        }

        public Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null)
            {
                return null;
            }

            _MakeEntity();
            return _behaviour.StartCoroutine(routine);
        }

        public Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            _MakeEntity();
            return _behaviour.StartCoroutine(methodName, value);
        }

        public void StopCoroutine(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return;
            }

            if (_entity != null)
            {
                _behaviour.StopCoroutine(methodName);
            }
        }

        public void StopCoroutine(IEnumerator routine)
        {
            if (routine == null)
            {
                return;
            }

            if (_entity != null)
            {
                _behaviour.StopCoroutine(routine);
            }
        }

        public void StopCoroutine(Coroutine routine)
        {
            if (routine == null)
                return;

            if (_entity != null)
            {
                _behaviour.StopCoroutine(routine);
                routine = null;
            }
        }

        public void StopAllCoroutines()
        {
            if (_entity != null)
            {
                _behaviour.StopAllCoroutines();
            }
        }

        #endregion

        #region 注入 Unity Update [INJECT UNITY UPDATE]

        /// <summary>
        /// 为给外部提供的 添加帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddUpdateListener(Action action)
        {
            _MakeEntity();
            AddUpdateListenerImp(action).Forget();
        }

        private async UniTaskVoid AddUpdateListenerImp(Action action)
        {
            await UniTask.Yield();
            _behaviour.AddUpdateListener(action);
        }

        /// <summary>
        /// 为给外部提供的 添加物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddFixedUpdateListener(Action action)
        {
            _MakeEntity();
            AddFixedUpdateListenerImp(action).Forget();
        }

        private async UniTaskVoid AddFixedUpdateListenerImp(Action action)
        {
            await UniTask.Yield(PlayerLoopTiming.LastEarlyUpdate);
            _behaviour.AddFixedUpdateListener(action);
        }

        /// <summary>
        /// 为给外部提供的 添加Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddLateUpdateListener(Action action)
        {
            _MakeEntity();
            AddLateUpdateListenerImp(action).Forget();
        }

        private async UniTaskVoid AddLateUpdateListenerImp(Action action)
        {
            await UniTask.Yield();
            _behaviour.AddLateUpdateListener(action);
        }

        /// <summary>
        /// 移除帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveUpdateListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveUpdateListener(action);
        }

        /// <summary>
        /// 移除物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveFixedUpdateListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveFixedUpdateListener(action);
        }

        /// <summary>
        /// 移除Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveLateUpdateListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveLateUpdateListener(action);
        }

        #endregion

        #region Unity 事件注入 [UNITY EVENTS INJECT]

        /// <summary>
        /// 为给外部提供的Destroy注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddDestroyListener(Action action)
        {
            _MakeEntity();
            _behaviour.AddDestroyListener(action);
        }

        /// <summary>
        /// 为给外部提供的Destroy反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveDestroyListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveDestroyListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnDrawGizmos注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnDrawGizmosListener(Action action)
        {
            _MakeEntity();
            _behaviour.AddOnDrawGizmosListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnDrawGizmos反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnDrawGizmosListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveOnDrawGizmosListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnDrawGizmosSelectedListener(Action action)
        {
            _MakeEntity();
            _behaviour.AddOnDrawGizmosSelectedListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnDrawGizmosSelectedListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveOnDrawGizmosSelectedListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnApplicationPause注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnApplicationPauseListener(Action<bool> action)
        {
            _MakeEntity();
            _behaviour.AddOnApplicationPauseListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnApplicationPause反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnApplicationPauseListener(Action<bool> action)
        {
            _MakeEntity();
            _behaviour.RemoveOnApplicationPauseListener(action);
        }

        #endregion

        private void _MakeEntity()
        {
            if (_entity != null)
            {
                return;
            }

            _entity = new GameObject("[UpdateDriver]");
            _entity.SetActive(true);
            Object.DontDestroyOnLoad(_entity);
            _behaviour = _entity.AddComponent<MainBehaviour>();
        }

        private class MainBehaviour : MonoBehaviour
        {
            private event Action OnUpdateEvent;
            private event Action OnFixedUpdateEvent;
            private event Action OnLateUpdateEvent;
            private event Action OnDestroyEvent;
            private event Action OnDrawGizmosEvent;
            private event Action OnDrawGizmosSelectedEvent;
            private event Action<bool> OnApplicationPauseEvent;

            void Update()
            {
                if (OnUpdateEvent != null)
                {
                    OnUpdateEvent();
                }
            }

            void FixedUpdate()
            {
                if (OnFixedUpdateEvent != null)
                {
                    OnFixedUpdateEvent();
                }
            }

            void LateUpdate()
            {
                if (OnLateUpdateEvent != null)
                {
                    OnLateUpdateEvent();
                }
            }

            private void OnDestroy()
            {
                if (OnDestroyEvent != null)
                {
                    OnDestroyEvent();
                }
            }

            [Conditional("UNITY_EDITOR")]
            private void OnDrawGizmos()
            {
                if (OnDrawGizmosEvent != null)
                {
                    OnDrawGizmosEvent();
                }
            }

            [Conditional("UNITY_EDITOR")]
            private void OnDrawGizmosSelected()
            {
                if (OnDrawGizmosSelectedEvent != null)
                {
                    OnDrawGizmosSelectedEvent();
                }
            }

            private void OnApplicationPause(bool pauseStatus)
            {
                if (OnApplicationPauseEvent != null)
                {
                    OnApplicationPauseEvent(pauseStatus);
                }
            }

            public void AddLateUpdateListener(Action action)
            {
                OnLateUpdateEvent += action;
            }

            public void RemoveLateUpdateListener(Action action)
            {
                OnLateUpdateEvent -= action;
            }

            public void AddFixedUpdateListener(Action action)
            {
                OnFixedUpdateEvent += action;
            }

            public void RemoveFixedUpdateListener(Action action)
            {
                OnFixedUpdateEvent -= action;
            }

            public void AddUpdateListener(Action action)
            {
                OnUpdateEvent += action;
            }

            public void RemoveUpdateListener(Action action)
            {
                OnUpdateEvent -= action;
            }

            public void AddDestroyListener(Action action)
            {
                OnDestroyEvent += action;
            }

            public void RemoveDestroyListener(Action action)
            {
                OnDestroyEvent -= action;
            }

            [Conditional("UNITY_EDITOR")]
            public void AddOnDrawGizmosListener(Action action)
            {
                OnDrawGizmosEvent += action;
            }

            [Conditional("UNITY_EDITOR")]
            public void RemoveOnDrawGizmosListener(Action action)
            {
                OnDrawGizmosEvent -= action;
            }

            [Conditional("UNITY_EDITOR")]
            public void AddOnDrawGizmosSelectedListener(Action action)
            {
                OnDrawGizmosSelectedEvent += action;
            }

            [Conditional("UNITY_EDITOR")]
            public void RemoveOnDrawGizmosSelectedListener(Action action)
            {
                OnDrawGizmosSelectedEvent -= action;
            }

            public void AddOnApplicationPauseListener(Action<bool> action)
            {
                OnApplicationPauseEvent += action;
            }

            public void RemoveOnApplicationPauseListener(Action<bool> action)
            {
                OnApplicationPauseEvent -= action;
            }

            public void Release()
            {
                OnUpdateEvent = null;
                OnFixedUpdateEvent = null;
                OnLateUpdateEvent = null;
                OnDrawGizmosEvent = null;
                OnDrawGizmosSelectedEvent = null;
                OnDestroyEvent = null;
                OnApplicationPauseEvent = null;
            }
        }
    }
}
