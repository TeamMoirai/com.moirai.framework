using Moirai.Atropos.FrameLoop;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// GameApp 的轻量 MonoBehaviour 宿主：只承接 Unity 仅在 MonoBehaviour 上派发的消息。
    /// <para>承载 <b>协程</b> / <b>OnDrawGizmos</b> / <b>OnDrawGizmosSelected</b> / <b>OnApplicationPause</b>，
    /// 后三者一律转发到 <see cref="PlayerLoopDriver"/> 的静态事件表——订阅不存于本宿主。</para>
    /// <para>帧逻辑订阅（Update / FixedUpdate / LateUpdate / Destroy）由 PlayerLoop 直接驱动，与本宿主无关：
    /// 宿主被场景切换或人为销毁时，只会中断协程与这三类引擎消息，订阅本身不丢失，
    /// 重建宿主即恢复派发。</para>
    /// <para>继承 <see cref="SingletonMono_Persistent{T}"/>：跨场景存活，被销毁后经
    /// <see cref="SingletonMono{T}.Instance"/> 惰性重建；<see cref="GameApp"/> 只经由本类访问，不直接持有 Mono。</para>
    /// </summary>
    internal sealed class GameAppHost : SingletonMono_Persistent<GameAppHost>
    {
        /// <summary>GameObject 显示名（覆盖基类的 AutoCreated 命名）。</summary>
        private const string HOST_OBJECT_NAME = "[GameAppHost]";

        /// <summary>
        /// 在主线程物化宿主（幂等）。
        /// <para>由 <see cref="GameApp.Initialize"/> 及各 Add*Listener 调用：一是保证 ApplicationPause /
        /// Gizmos 从一开始就有派发者，二是把实例化锁定在主线程——<see cref="SingletonMono{T}.Instance"/>
        /// 在物化前被后台线程访问会抛出 <see cref="GameException"/>。</para>
        /// </summary>
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

        /// <summary>
        /// SubsystemRegistration：复位基类退出标记。
        /// <para>关闭 Domain Reload 时静态字段跨 Play 会话保留，不复位将令 <see cref="SingletonMono{T}.Instance"/>
        /// 永久返回 null，宿主再也无法重建。</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_ShuttingDown = false;
        }

        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            gameObject.name = HOST_OBJECT_NAME;
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
