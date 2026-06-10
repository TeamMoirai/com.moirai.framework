using UnityEngine;
using Moirai.Atropos.Pool;

namespace Moirai.Atropos.Schedulers
{
    /// <summary>
    /// 允许在不使用 <see cref="Coroutine"/> 或 <see cref="MonoBehaviour"/> 的情况下延迟运行事件。
    /// </summary>
    /// <remarks>要创建并启动 Timer，请使用 <see cref="Register"/> 方法。</remarks>
    /// <!-- 参考自 https://github.com/akbiggs/UnityTimer -->
    internal class Timer : IScheduled
    {
        private static readonly _ObjectPool<Timer> Pool = new _ObjectPool<Timer>(() => new Timer());
        
        #region Public Properties/Fields
        public SchedulerHandle Handle { get; private set; }
        
        /// <summary>
        /// 计时器从开始到结束需要多长时间才能完成。
        /// </summary>
        public float Duration { get; private set; }

        /// <summary>
        /// 计时器是否在完成后再次运行。
        /// </summary>
        public bool IsLooped { get; set; }

        /// <summary>
        /// 计时器是否完成运行。如果 timer 已取消，则为 <c>false</c>。
        /// </summary>
        public bool IsCompleted { get; private set; }

        /// <summary>
        /// 计时器使用的是实时时间还是游戏时间。
        /// Real time 不受游戏时间刻度变化（例如暂停、慢动作）的影响，而游戏时间则受到影响。
        /// </summary>
        public bool UsesRealTime { get; private set; }

        /// <summary>
        /// 计时器当前是否暂停。
        /// </summary>
        public bool IsPaused => _timeElapsedBeforePause.HasValue;

        /// <summary>
        /// 计时器是否已取消。
        /// </summary>
        public bool IsCancelled => _timeElapsedBeforeCancel.HasValue;

        public bool IsDone => IsCompleted || IsCancelled;

        #endregion
        
        #region Public Static Methods
        /// <summary>
        /// 注册一个新的计时器，该计时器应在经过一定时间后触发事件。
        ///
        /// 当场景更改时，已注册的计时器将被销毁。
        /// </summary>
        /// <param name="duration">计时器应触发之前等待的时间（以秒为单位）。</param>
        /// <param name="onComplete">计时器完成时触发的操作。</param>
        /// <param name="onUpdate">每次更新计时器时应触发的操作。采用自计时器的当前循环开始以来经过的时间（以秒为单位）。</param>
        /// <param name="tickFrame">计数器 tick 在哪一帧上</param>
        /// <param name="isLooped">执行后是否应重复计时器。</param>
        /// <param name="useRealTime">计时器是使用实时时间（即不受暂停 - pause、慢/快动作 - slow/fast-motion 影响）还是游戏时间（将受影响）。</param>
        /// <returns>计时器对象，允许检查状态和停止/恢复进度。</returns>
        internal static Timer Register(float duration, SchedulerUnsafeBinding onComplete, SchedulerUnsafeBinding<float> onUpdate,
           TickFrame tickFrame = TickFrame.Update, bool isLooped = false, bool useRealTime = false)
        {
            Timer timer = Pool.Get();
            timer.Init(SchedulerRunner.Get().NewHandle(), duration, ref onComplete, ref onUpdate, isLooped, useRealTime);
            SchedulerRunner.Get().Register(timer, tickFrame, onComplete.IsValid() ? onComplete.GetDelegate() : onUpdate.GetDelegate());
            return timer;
        }
        #endregion
        
        #region 公共方法 [PUBLIC METHODS]
        /// <summary>
        /// Stop a timer that is in-progress or paused. The timer's on completion callback will not be called.
        /// </summary>
        public void Cancel()
        {
            if (IsDone) return;
            _timeElapsedBeforeCancel = GetTimeElapsed();
            _timeElapsedBeforePause = null;
        }
        public void Dispose()
        {
            SchedulerRunner.Get().Unregister(this, _onComplete.IsValid() ? _onComplete.GetDelegate() : _onUpdate.GetDelegate());
            _onUpdate = default;
            _onComplete = default;
            Pool.Release(this);
        }
        /// <summary>
        /// Pause a running timer. A paused timer can be resumed from the same point it was paused.
        /// </summary>
        public void Pause()
        {
            if (IsPaused || IsDone)
            {
                return;
            }

            _timeElapsedBeforePause = GetTimeElapsed();
        }

        /// <summary>
        /// Continue a paused timer. Does nothing if the timer has not been paused.
        /// </summary>
        public void Resume()
        {
            if (!IsPaused || IsDone)
            {
                return;
            }

            _timeElapsedBeforePause = null;
        }

        /// <summary>
        /// Get how many seconds have elapsed since the start of this timer's current cycle.
        /// </summary>
        /// <returns>The number of seconds that have elapsed since the start of this timer's current cycle, i.e.
        /// the current loop if the timer is looped, or the start if it isn't.
        ///
        /// If the timer has finished running, this is equal to the duration.
        ///
        /// If the timer was cancelled/paused, this is equal to the number of seconds that passed between the timer
        /// starting and when it was cancelled/paused.</returns>
        public float GetTimeElapsed()
        {
            if (IsCompleted || GetWorldTime() >= GetFireTime())
            {
                return Duration;
            }

            return _timeElapsedBeforeCancel ??
                   _timeElapsedBeforePause ??
                   GetWorldTime() - _startTime;
        }

        /// <summary>
        /// Get how many seconds remain before the timer completes.
        /// </summary>
        /// <returns>The number of seconds that remain to be elapsed until the timer is completed. A timer
        /// is only elapsing time if it is not paused, cancelled, or completed. This will be equal to zero
        /// if the timer completed.</returns>
        public float GetTimeRemaining()
        {
            return Duration - GetTimeElapsed();
        }

        /// <summary>
        /// Get how much progress the timer has made from start to finish as a ratio.
        /// </summary>
        /// <returns>A value from 0 to 1 indicating how much of the timer's duration has been elapsed.</returns>
        public float GetRatioComplete()
        {
            return GetTimeElapsed() / Duration;
        }

        /// <summary>
        /// Get how much progress the timer has left to make as a ratio.
        /// </summary>
        /// <returns>A value from 0 to 1 indicating how much of the timer's duration remains to be elapsed.</returns>
        public float GetRatioRemaining()
        {
            return GetTimeRemaining() / Duration;
        }

        #endregion
        
        #region Private Properties/Fields
        private SchedulerUnsafeBinding _onComplete;
        private SchedulerUnsafeBinding<float> _onUpdate;
        private float _startTime;
        private float _lastUpdateTime;

        // for pausing, we push the start time forward by the amount of time that has passed.
        // this will mess with the amount of time that elapsed when we're cancelled or paused if we just
        // check the start time versus the current world time, so we need to cache the time that was elapsed
        // before we paused/cancelled
        private float? _timeElapsedBeforeCancel;
        private float? _timeElapsedBeforePause;

        #endregion
        
        #region Private Constructor (use static Register method to create new timer)

        private void Init(SchedulerHandle handle, float duration, ref SchedulerUnsafeBinding onComplete, ref SchedulerUnsafeBinding<float> onUpdate,
            bool isLooped, bool usesRealTime)
        {
            Handle = handle;
            Duration = duration;
            _onComplete = onComplete;
            _onUpdate = onUpdate;

            IsLooped = isLooped;
            UsesRealTime = usesRealTime;

            _startTime = GetWorldTime();
            _lastUpdateTime = _startTime;

            IsCompleted = false;
            _timeElapsedBeforeCancel = null;
            _timeElapsedBeforePause = null;
        }

        #endregion
        
        #region 私有方法 [PRIVATE METHODS]
        private float GetWorldTime()
        {
            return UsesRealTime ? Time.realtimeSinceStartup : Time.time;
        }

        private float GetFireTime()
        {
            return _startTime + Duration;
        }

        private float GetTimeDelta()
        {
            return GetWorldTime() - _lastUpdateTime;
        }

        public void Update()
        {
            if (IsDone)
            {
                return;
            }

            if (IsPaused)
            {
                _startTime += GetTimeDelta();
                _lastUpdateTime = GetWorldTime();
                return;
            }

            _lastUpdateTime = GetWorldTime();

            _onUpdate.Invoke(GetTimeElapsed());

            if (GetWorldTime() >= GetFireTime())
            {
                _onComplete.Invoke();

                if (IsLooped)
                {
                    _startTime = GetWorldTime();
                }
                else
                {
                    IsCompleted = true;
                }
            }
        }

        #endregion

    }
}