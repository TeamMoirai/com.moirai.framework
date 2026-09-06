using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 帧率采样器（按固定间隔窗口聚合，零分配热路径）。
    /// </summary>
    public sealed class FpsCounter
    {
        #region 字段 [FIELDS]

        private float _updateInterval;
        private float _currentFps;
        private int _frames;
        private float _accumulator;
        private float _timeLeft;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化帧率采样器的新实例。
        /// </summary>
        /// <param name="updateInterval">刷新间隔（秒，须为正）。</param>
        public FpsCounter(float updateInterval)
        {
            if (updateInterval <= 0f)
            {
                LogUtility.Error("Update interval is invalid.");
                return;
            }

            _updateInterval = updateInterval;
            Reset();
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取或设置刷新间隔（秒）。
        /// </summary>
        public float UpdateInterval
        {
            get
            {
                return _updateInterval;
            }
            set
            {
                if (value <= 0f)
                {
                    LogUtility.Error("Update interval is invalid.");
                    return;
                }

                _updateInterval = value;
                Reset();
            }
        }

        /// <summary>
        /// 获取当前帧率。
        /// </summary>
        public float CurrentFps
        {
            get
            {
                return _currentFps;
            }
        }

        #endregion

        #region 轮询 [TICK]

        /// <summary>
        /// 逐帧驱动采样（每帧调用）。
        /// </summary>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        public void Update(float realElapseSeconds)
        {
            _frames++;
            _accumulator += realElapseSeconds;
            _timeLeft -= realElapseSeconds;

            if (_timeLeft <= 0f)
            {
                _currentFps = _accumulator > 0f ? _frames / _accumulator : 0f;
                _frames = 0;
                _accumulator = 0f;
                _timeLeft += _updateInterval;
            }
        }

        #endregion

        #region 私有 [PRIVATE]

        private void Reset()
        {
            _currentFps = 0f;
            _frames = 0;
            _accumulator = 0f;
            _timeLeft = 0f;
        }

        #endregion
    }
}
