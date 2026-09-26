using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 此结构体包含所有按钮状态，这些状态会逐帧进行更新。
    /// </summary>
    /// <remarks>
    /// 帧序契约（必须严格遵守，否则边沿标志与 IsPressed 等派生属性将失效）：
    /// 每帧先写入 <see cref="Value"/>，再调用 <see cref="Update"/>，随后读取
    /// <see cref="Started"/>/<see cref="Canceled"/> 等状态，最后调用 <see cref="Reset"/>
    /// 清除边沿标志，等待下一帧。<see cref="Update"/> 内部以上一帧值为基准累积边沿，
    /// 不调用 <see cref="Reset"/> 会使 <see cref="Started"/> 恒为 true、<see cref="IsPressed"/> 恒为 false。
    /// </remarks>
    [System.Serializable]
    public struct BoolAction
    {
        [SerializeField] private bool m_Value;

        /// <summary>
        /// 动作的当前值。
        /// </summary>
        public bool Value
        {
            get => m_Value;
            set => m_Value = value;
        }
        
        /// <summary>
        /// 如果值从 <c>false</c> 转变为 <c>true</c>（例如按钮按下），则返回 <c>true</c>。
        /// </summary>
        public bool Started { get; private set; }

        /// <summary>
        /// 如果值从 <c>true</c> 转变为 <c>false</c>（例如按钮释放），则返回 <c>true</c>。
        /// </summary>
        public bool Canceled { get; private set; }

        /// <summary>
        /// 自上次<c><see cref="Started"/></c>标志设置以来经过的时间。
        /// </summary>
        public float StartedElapsedTime { get; private set; }

        /// <summary>
        /// 自上次<c><see cref="Canceled"/></c>标志设置以来经过的时间。
        /// </summary>
        public float CanceledElapsedTime { get; private set; }

        /// <summary>
        /// 自此动作设置为 <c>true</c> 以来经过的时间。
        /// </summary>
        public float ActiveTime { get; private set; }

        /// <summary>
        /// 自此动作设置为 <c>false</c> 以来经过的时间。
        /// </summary>
        public float InactiveTime { get; private set; }

        /// <summary>
        /// 此动作在<c><see cref="Canceled"/></c>时记录的最后一个<c><see cref="ActiveTime"/></c>值。
        /// </summary>
        public float LastActiveTime { get; private set; }

        /// <summary>
        /// 此动作在<c><see cref="Started"/></c>时记录的最后一个<c><see cref="InactiveTime"/></c>值。
        /// </summary>
        public float LastInactiveTime { get; private set; }

        // 上一帧的值
        private bool _previousValue;
        // 上一帧的“Started”状态
        private bool _previousStarted;
        // 上一帧的“Canceled”状态
        private bool _previousCanceled;

        /// <!-- 常用按键状态 -->

        /// <summary>如果在此帧被按下，则返回 <c>true</c></summary>
        public bool IsDown => Started;
        /// <summary>如果在此帧前就被按下，则返回 <c>true</c></summary>
        public bool IsPressed => m_Value && !IsDown;
        /// <summary>如果在此帧被松开，则返回 <c>true</c></summary>
        public bool IsUp => Canceled;
        /// <summary>如果在此帧前就被松开，则返回 <c>true</c></summary>
        public bool IsOff => !m_Value && !IsUp;

        /// <summary>
        /// 初始化值。
        /// </summary>
        public void Initialize()
        {
            StartedElapsedTime = Mathf.Infinity;
            CanceledElapsedTime = Mathf.Infinity;

            m_Value = false;
            _previousValue = false;
            _previousStarted = false;
            _previousCanceled = false;
        }

        /// <summary>
        /// 重置动作。
        /// </summary>
        /// <remarks>清除 <see cref="Started"/>/<see cref="Canceled"/> 边沿标志；必须在每帧读取状态之后调用（详见类型级帧序契约）。</remarks>
        public void Reset()
        {
            Started = false;
            Canceled = false;
        }

        /// <summary>
        /// 根据当前按钮状态更新字段。
        /// </summary>
        public void Update(float dt)
        {
            Started |= !_previousValue && m_Value;
            Canceled |= _previousValue && !m_Value;

            StartedElapsedTime += dt;
            CanceledElapsedTime += dt;

            if (Started)
            {
                StartedElapsedTime = 0f;

                if (!_previousStarted)
                {
                    LastActiveTime = 0f;
                    LastInactiveTime = InactiveTime;
                }
            }

            if (Canceled)
            {
                CanceledElapsedTime = 0f;

                if (!_previousCanceled)
                {
                    LastActiveTime = ActiveTime;
                    LastInactiveTime = 0f;
                }
            }

            if (m_Value)
            {
                ActiveTime += dt;
                InactiveTime = 0f;
            }
            else
            {
                ActiveTime = 0f;
                InactiveTime += dt;
            }

            // 更新上一帧的值和状态
            _previousValue = m_Value;
            _previousStarted = Started;
            _previousCanceled = Canceled;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // EDITOR ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────


#if UNITY_EDITOR
    
    [UnityEditor.CustomPropertyDrawer(typeof(BoolAction))]
    public class BoolActionEditor : ActionPropertyDrawer { }

#endif
}
