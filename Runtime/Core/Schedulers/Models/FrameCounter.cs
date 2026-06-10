using Moirai.Atropos.Pool;
using UnityEngine;

namespace Moirai.Atropos.Schedulers
{
    internal class FrameCounter : IScheduled
    {
        private static readonly _ObjectPool<FrameCounter> Pool = new _ObjectPool<FrameCounter>(() => new FrameCounter());
      
        #region Public Properties/Fields
        public SchedulerHandle Handle { get; private set; }
        
        /// <summary>
        /// 计数器从开始到结束需要多少帧才能完成。
        /// </summary>
        public int Frame { get; private set; }
        
        private int _count;
        
        /// <summary>
        /// 计数器是否在完成后再次运行。
        /// </summary>
        public bool IsLooped { get; set; }

        /// <summary>
        /// 计数器是否已完成运行。如果 counter 已取消，则为 false。
        /// </summary>
        public bool IsCompleted { get; private set; }

        /// <summary>
        /// 计数器当前是否暂停。
        /// </summary>
        public bool IsPaused => _timeElapsedBeforePause.HasValue;

        /// <summary>
        /// 计数器是否已取消。
        /// </summary>
        public bool IsCancelled => _timeElapsedBeforeCancel.HasValue;

        public bool IsDone => IsCompleted || IsCancelled;

        #endregion
        
        #region Public Static Methods
        /// <summary>
        /// 注册一个新计数器，该计数器应在经过一定帧后触发事件。
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="onComplete"></param>
        /// <param name="onUpdate"></param>
        /// <param name="tickFrame"></param>
        /// <param name="isLooped"></param>
        /// <returns></returns>
        internal static FrameCounter Register(int frame, SchedulerUnsafeBinding onComplete, SchedulerUnsafeBinding<int> onUpdate,
         TickFrame tickFrame = TickFrame.Update, bool isLooped = false)
        {
            FrameCounter timer = Pool.Get();
            timer.Init(SchedulerRunner.Get().NewHandle(), frame, ref onComplete, ref onUpdate, isLooped);
            SchedulerRunner.Get().Register(timer, tickFrame, onComplete.IsValid() ? onComplete.GetDelegate() : onUpdate.GetDelegate());
            return timer;
        }
        #endregion
        
        #region 公共方法 [PUBLIC METHODS]
        
        /// <summary>
        /// 停止正在进行或已暂停的计数器。不会调用计数器的 on completion 回调。
        /// </summary>
        public void Cancel()
        {
            if (IsDone) return;
            _timeElapsedBeforeCancel = Time.time;
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
        /// 暂停正在运行的计数器。暂停的计数器可以从暂停的同一点恢复。
        /// </summary>
        public void Pause()
        {
            if (IsPaused || IsDone)
            {
                return;
            }

            _timeElapsedBeforePause = Time.time;
        }

        /// <summary>
        /// 继续暂停的计数器。如果计数器尚未暂停，则不执行任意操作。
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
        /// 获取计数器完成之前剩余的帧数。
        /// </summary>
        /// <returns></returns>
        public float GetFrameRemaining()
        {
            return Frame - _count;
        }

        #endregion
        
        #region Private Properties/Fields
        private SchedulerUnsafeBinding _onComplete;
        private SchedulerUnsafeBinding<int> _onUpdate;
        private float? _timeElapsedBeforeCancel;
        private float? _timeElapsedBeforePause;
        #endregion
        
        #region Private Constructor
        private void Init(SchedulerHandle handle, int frame, ref SchedulerUnsafeBinding onComplete, ref SchedulerUnsafeBinding<int> onUpdate, bool isLooped)
        {
            Handle = handle;
            Frame = frame;
            _onComplete = onComplete;
            _onUpdate = onUpdate;

            IsLooped = isLooped;

            _count = 0;

            IsCompleted = false;
            _timeElapsedBeforeCancel = null;
            _timeElapsedBeforePause = null;
        }

        #endregion

        public void Update()
        {
            if (IsDone)
            {
                return;
            }

            if (IsPaused)
            {
                return;
            }

            ++_count;

            _onUpdate.Invoke(_count);

            if (_count >= Frame)
            {
                _onComplete.Invoke();

                if (IsLooped)
                {
                    _count = 0;
                }
                else
                {
                    IsCompleted = true;
                }
            }
        }

    }
}