using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos
{
    public partial class GameApp
    {
        private sealed class MainBehaviour : MonoBehaviour
        {
            private event Action OnUpdateEvent;
            private event Action OnFixedUpdateEvent;
            private event Action OnLateUpdateEvent;
            private event Action OnDestroyEvent;
            private event Action OnDrawGizmosEvent;
            private event Action OnDrawGizmosSelectedEvent;
            private event Action<bool> OnApplicationPauseEvent;
            private event Action OnApplicationQuitEvent;
            private event Action<bool> OnApplicationFocusEvent;

            #region 引擎方法 [UNITY METHODS]

            private void OnDestroy()
            {
                OnDestroyEvent?.Invoke();
            }

            private void Update()
            {
                OnUpdateEvent?.Invoke();
            }

            private void FixedUpdate()
            {
                OnFixedUpdateEvent?.Invoke();
            }

            private void LateUpdate()
            {
                OnLateUpdateEvent?.Invoke();
            }

            [Conditional("UNITY_EDITOR")]
            private void OnDrawGizmos()
            {
                OnDrawGizmosEvent?.Invoke();
            }

            [Conditional("UNITY_EDITOR")]
            private void OnDrawGizmosSelected()
            {
                OnDrawGizmosSelectedEvent?.Invoke();
            }

            private void OnApplicationPause(bool pauseStatus)
            {
                OnApplicationPauseEvent?.Invoke(pauseStatus);
            }

            private void OnApplicationQuit()
            {
                OnApplicationQuitEvent?.Invoke();
                StopAllCoroutines();
            }

            private void OnApplicationFocus(bool hasFocus)
            {
                OnApplicationFocusEvent?.Invoke(hasFocus);
            }

            #endregion

            #region 公共方法 [PUBLIC METHODS]

            public void AddUpdateEvent(Action action)
            {
                OnUpdateEvent += action;
            }

            public void RemoveUpdateEvent(Action action)
            {
                OnUpdateEvent -= action;
            }

            public void AddFixedUpdateEvent(Action action)
            {
                OnFixedUpdateEvent += action;
            }

            public void RemoveFixedUpdateEvent(Action action)
            {
                OnFixedUpdateEvent -= action;
            }

            public void AddLateUpdateEvent(Action action)
            {
                OnLateUpdateEvent += action;
            }

            public void RemoveLateUpdateEvent(Action action)
            {
                OnLateUpdateEvent -= action;
            }

            public void AddDestroyEvent(Action action)
            {
                OnDestroyEvent += action;
            }

            public void RemoveDestroyEvent(Action action)
            {
                OnDestroyEvent -= action;
            }

            [Conditional("UNITY_EDITOR")]
            public void AddDrawGizmosEvent(Action action)
            {
                OnDrawGizmosEvent += action;
            }

            [Conditional("UNITY_EDITOR")]
            public void RemoveDrawGizmosEvent(Action action)
            {
                OnDrawGizmosEvent -= action;
            }

            [Conditional("UNITY_EDITOR")]
            public void AddDrawGizmosSelectedEvent(Action action)
            {
                OnDrawGizmosSelectedEvent += action;
            }

            [Conditional("UNITY_EDITOR")]
            public void RemoveDrawGizmosSelectedEvent(Action action)
            {
                OnDrawGizmosSelectedEvent -= action;
            }

            public void AddApplicationPauseEvent(Action<bool> action)
            {
                OnApplicationPauseEvent += action;
            }

            public void RemoveApplicationPauseEvent(Action<bool> action)
            {
                OnApplicationPauseEvent -= action;
            }

            public void AddApplicationQuitEvent(Action action)
            {
                OnApplicationQuitEvent += action;
            }

            public void RemoveApplicationQuitEvent(Action action)
            {
                OnApplicationQuitEvent -= action;
            }

            public void AddApplicationFocusEvent(Action<bool> action)
            {
                OnApplicationFocusEvent += action;
            }

            public void RemoveApplicationFocusEvent(Action<bool> action)
            {
                OnApplicationFocusEvent -= action;
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
                OnApplicationQuitEvent = null;
                OnApplicationFocusEvent = null;
            }

            #endregion
        }
    }
}