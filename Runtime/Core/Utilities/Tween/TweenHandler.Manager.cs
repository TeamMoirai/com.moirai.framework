using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    public partial class TweenHandler
    {
        /// <summary>
        /// Tween 管理器，定期调用已注册 handler 的 ReleaseUnusedTween。
        /// </summary>
        public class TweenManager
        {
            private static readonly List<TweenHandler> s_Handlers = new();
            private float _timer;
            private float _checkInterval = 60f;

            private static TweenManager s_Instance;

            /// <summary>
            /// 关闭域重载时进入 Play 的静态清理：清空注册表与实例，
            /// 防止陈旧 handler 引用与重复定时器。
            /// </summary>
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetStatics()
            {
                s_Handlers.Clear();
                s_Instance = null;
            }

            public static void EnsureInstance()
            {
                if (s_Instance != null) return;

                s_Instance = new TweenManager();
                GameApp.AddUpdateListener(s_Instance.Update);
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
                // 定期清理已失效的 tween 缓存
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
}
