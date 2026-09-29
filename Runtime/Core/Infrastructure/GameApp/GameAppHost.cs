using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// GameApp 的轻量 MonoBehaviour 宿主：只承接 Unity 仅在 MonoBehaviour 上派发的消息。
    /// </summary>
    /// <remarks>
    /// 承载协程与 <c>OnDrawGizmos</c> / <c>OnDrawGizmosSelected</c> / <c>OnApplicationPause</c>，后三者转发到 <see cref="PlayerLoopDriver"/> 的静态事件表。
    /// 帧逻辑订阅由 PlayerLoop 直接驱动、存于静态表：宿主被销毁只中断协程与上述引擎消息，订阅不丢，重建宿主即恢复派发。
    /// 跨场景存活（<see cref="SingletonMono_Persistent{T}"/>），被销毁后经 <see cref="SingletonMono{T}.Instance"/> 惰性重建。
    /// </remarks>
    internal sealed class GameAppHost : SingletonMono_Persistent<GameAppHost>
    {
        /// <summary>GameObject 显示名（覆盖基类的 AutoCreated 命名）。</summary>
        private const string HOST_OBJECT_NAME = "[GameAppHost]";

        /// <summary>
        /// 在主线程物化宿主（幂等）。
        /// </summary>
        /// <remarks>
        /// 由 <see cref="GameApp.Initialize"/> 及各 <c>Add*Listener</c> 调用，保证 Pause / Gizmos 从一开始就有派发者。
        /// 必须在主线程调用：<see cref="SingletonMono{T}.Instance"/> 在物化前被后台线程访问会抛 <see cref="GameException"/>。
        /// </remarks>
        internal static void Bootstrap()
        {
            _ = Instance;
        }

        /// <summary>销毁宿主（<see cref="GameApp.Shutdown"/> 调用）。幂等。</summary>
        internal static void Release()
        {
            GameAppHost host = s_Instance;
            if (host != null) Destroy(host.gameObject);
        }
        
        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            gameObject.name = HOST_OBJECT_NAME;
        }

        /// <summary>
        /// SubsystemRegistration：复位基类退出标记。
        /// </summary>
        /// <remarks>关闭 Domain Reload 时静态字段跨 Play 会话保留，不复位会让 <see cref="SingletonMono{T}.Instance"/> 永久返回 null。</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_ShuttingDown = false;
        }        
        
        #region 引擎方法 [UNITY METHODS]

        /// <summary>Unity 无纯 C# 的暂停事件，只能由宿主转发到静态表。</summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            PlayerLoopDriver.RaiseApplicationPause(pauseStatus);
        }

        // 以下两个消息仅在编辑器派发；打包后静态表为空，无转发者亦无副作用。
        private void OnDrawGizmos()
        {
            PlayerLoopDriver.RaiseDrawGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            PlayerLoopDriver.RaiseDrawGizmosSelected();
        }

        #endregion
    }
}
