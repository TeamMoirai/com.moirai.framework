using System;

namespace Moirai.Atropos
{
    [Serializable]
    public abstract class FrameworkHandler
    {
        [NonSerialized] private bool _initialized;

        /// <summary>
        /// 初始化处理器。
        /// </summary>
        internal void Internal_Init()
        {
            if (_initialized) return;

            OnInit();
            _initialized = true;
        }

        /// <summary>
        /// 关闭处理器。
        /// </summary>
        internal void Internal_Shutdown()
        {
            if (!_initialized) return;

            _initialized = false;
            OnShutdown();
        }

        /// <summary>
        /// 初始化回调，用于接管后端资源。
        /// </summary>
        protected virtual void OnInit()
        {
        }

        /// <summary>
        /// 关闭回调，用于释放后端资源。仅在处理器被替换或应用退出时调用。
        /// </summary>
        protected virtual void OnShutdown()
        {
        }
    }
}