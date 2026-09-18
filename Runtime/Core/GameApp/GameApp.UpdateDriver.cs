using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos
{
    public partial class GameApp
    {
        /// <summary>
        /// 协程 / 编辑器 Gizmos / ApplicationPause 轻量宿主。
        /// <para>帧逻辑订阅不在本类——由 <see cref="FrameLoop.PlayerLoopDriver"/> 静态注册表驱动。
        /// 本宿主被场景切换销毁时只影响协程与 Gizmos；订阅不会丢失。</para>
        /// </summary>
        private sealed class CoroutineHost : MonoBehaviour
        {
            private event Action OnDrawGizmosEvent;
            private event Action OnDrawGizmosSelectedEvent;
            private event Action<bool> OnApplicationPauseEvent;

            #region 引擎方法 [UNITY METHODS]

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

            #endregion

            #region 公共方法 [PUBLIC METHODS]

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

            #endregion
        }
    }
}
