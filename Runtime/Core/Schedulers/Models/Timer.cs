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
        private static readonly _ObjectPool<Timer> s_Pool = new _ObjectPool<Timer>(() => new Timer());
        
        #region 公共属性/字段 [PUBLIC PROPS/FIELDS]
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
        
        #region 公共静态方法 [PUBLIC STATIC METHODS]

        /// <summary>
        /// 注册一个新的计时器，该计时器应在经过一定时间后触发事件。
        /// <br/>
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
            Timer timer = s_Pool.Get();
            timer.Init(SchedulerRunner.Get().NewHandle(), duration, ref onComplete, ref onUpdate, isLooped, useRealTime);
            SchedulerRunner.Get().Register(timer, tickFrame, onComplete.IsValid() ? onComplete.GetDelegate() : onUpdate.GetDelegate());
            return timer;
        }
        #endregion
        
        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 停止正在进行或已暂停的计时器。不会调用计时器的完成回调。
        /// </summary>
        public void Cancel()
        {
            if (IsDone) return;
            _timeElapsedBeforeCancel = GetTimeElapsed();
            _timeElapsedBeforePause = null;
        }

        public void Dispose()
        {
            // 幂等：重复 Dispose 不得二次入池（否则同一实例被两次 Get 取出，池污染）
            if (_disposed) return;
            _disposed = true;

            SchedulerRunner.Get().Unregister(this, _onComplete.IsValid() ? _onComplete.GetDelegate() : _onUpdate.GetDelegate());
            _onUpdate = default;
            _onComplete = default;
            s_Pool.Release(this);
        }

        /// <summary>
        /// 暂停正在运行的计时器。已暂停的计时器可以从暂停的同一点恢复。
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
        /// 继续已暂停的计时器。如果计时器尚未暂停，则不执行任何操作。
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
        /// 获取自计时器当前循环开始以来经过的秒数。
        /// </summary>
        /// <returns>自计时器当前循环开始以来经过的秒数，即如果计时器循环则返回当前循环，
        /// 否则返回开始以来的秒数。
        ///
        /// 如果计时器已完成运行，则该值等于持续时间。
        ///
        /// 如果计时器被取消/暂停，则该值等于计时器开始到被取消/暂停之间经过的秒数。</returns>
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
        /// 获取计时器完成前剩余的秒数。
        /// </summary>
        /// <returns>计时器完成前还需经过的秒数。计时器只有在未被暂停、取消或完成时才会计时。
        /// 如果计时器已完成，则该值等于零。</returns>
        public float GetTimeRemaining()
        {
            return Duration - GetTimeElapsed();
        }

        /// <summary>
        /// 获取计时器从开始到结束的进度比例。
        /// </summary>
        /// <returns>一个从 0 到 1 的值，表示计时器已耗时长的比例。</returns>
        public float GetRatioComplete()
        {
            return GetTimeElapsed() / Duration;
        }

        /// <summary>
        /// 获取计时器剩余进度的比例。
        /// </summary>
        /// <returns>一个从 0 到 1 的值，表示计时器剩余时长占持续时间的比例。</returns>
        public float GetRatioRemaining()
        {
            return GetTimeRemaining() / Duration;
        }

        #endregion
        
        #region 私有属性/字段 [PRIVATE PROPS/FIELDS]

        private SchedulerUnsafeBinding _onComplete;
        private SchedulerUnsafeBinding<float> _onUpdate;
        private float _startTime;
        private float _lastUpdateTime;
        // 双重 Dispose 防护（幂等标记；Init 时复位）
        private bool _disposed;

        // 暂停时，我们会将开始时间向前推进已流逝的时间量。
        // 如果只比较开始时间与当前世界时间，这会干扰被取消或暂停时经过的时间计算，
        // 因此我们需要缓存暂停/取消前已流逝的时间。
        private float? _timeElapsedBeforeCancel;
        private float? _timeElapsedBeforePause;

        #endregion
        
        #region 私有构造函数（使用静态 Register 方法创建） [PRIVATE CTOR]

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
            _disposed = false;
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