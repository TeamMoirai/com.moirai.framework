using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// Tween 清理管理器，定期调用已注册 handler 的 ReleaseUnusedTween。
    /// </summary>
    public class TweenManager
    {
        private static readonly List<TweenHandler> s_Handlers = new();
        private float _timer;
        private float _checkInterval = 60f;

        private static TweenManager s_Instance;
        public static void EnsureInstance()
        {
            if (s_Instance == null)
            {
                s_Instance = new TweenManager();
                UnityUtility.AddUpdateListener(s_Instance.Update);
            }
        }

        public static void Register(TweenHandler handler)
        {
            if (!s_Handlers.Contains(handler))
                s_Handlers.Add(handler);
        }

        public static void Unregister(TweenHandler handler)
        {
            s_Handlers.Remove(handler);
        }

        public static void SetCheckInterval(float interval)
        {
            if (s_Instance != null)
                s_Instance._checkInterval = interval;
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                for (int i = 0; i < s_Handlers.Count; i++)
                    s_Handlers[i].ReleaseUnusedTween();

                _timer = _checkInterval;
            }
        }
    }
}
