using System;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器触发所在的 PlayerLoop 阶段。
    /// <para>时间轮在 <see cref="TimerPhase.Update"/> 推进；Fixed/Late 阶段到期的时间计时器延后到对应 Tick 触发，
    /// 帧计时器则各自在对应 Tick 中逐帧推进。</para>
    /// </summary>
    public enum TimerPhase : byte
    {
        /// <summary>逻辑帧 Update（默认）。</summary>
        Update = 0,
        /// <summary>物理帧 FixedUpdate。</summary>
        FixedUpdate = 1,
        /// <summary>LateUpdate。</summary>
        LateUpdate = 2,
    }

    /// <summary>
    /// 零分配计时器回调绑定（函数指针优先，兼容 Action）。
    /// <para>热路径请使用 <c>delegate*</c> 或缓存方法组，禁止捕获闭包。</para>
    /// </summary>
    public readonly unsafe struct TimerUnsafeBinding
    {
        internal const byte TYPE_PTR = 0;
        internal const byte TYPE_DELEGATE = 1;

        private readonly byte _assigned;
        private readonly byte _mType;
        private readonly object _mObj;
        private readonly void* _mPtr;
        private readonly Action _mDelegate;

        internal readonly bool IsAssigned => _assigned != 0;
        internal readonly byte BindingType => _mType;
        internal readonly object Instance => _mObj;
        internal readonly void* FunctionPointer => _mPtr;
        internal readonly Action DelegateCallback => _mDelegate;

        public TimerUnsafeBinding(object instance, delegate* managed<object, void> mPtr)
        {
            _mType = TYPE_PTR;
            _mObj = instance;
            _mPtr = mPtr;
            _mDelegate = null;
            _assigned = 1;
        }

        public TimerUnsafeBinding(delegate* managed<void> mPtr)
        {
            _mType = TYPE_PTR;
            _mObj = null;
            _mPtr = mPtr;
            _mDelegate = null;
            _assigned = 1;
        }

        public TimerUnsafeBinding(Action @delegate)
        {
            _mType = TYPE_DELEGATE;
            _mObj = null;
            _mPtr = null;
            _mDelegate = @delegate;
            _assigned = 1;
        }

        public readonly void Invoke()
        {
            if (!IsValid()) return;
            if (_mType == TYPE_DELEGATE)
            {
                _mDelegate?.Invoke();
                return;
            }

            if (_mObj != null)
            {
                ((delegate* managed<object, void>)_mPtr)(_mObj);
            }
            else
            {
                ((delegate* managed<void>)_mPtr)();
            }
        }

        public readonly bool IsValid()
        {
            if (_assigned == 0) return false;
            if (_mType == TYPE_DELEGATE) return _mDelegate != null;
            return _mPtr != null;
        }

        public static implicit operator TimerUnsafeBinding(Action action) => new(action);

        public static implicit operator TimerUnsafeBinding(delegate* managed<void> ptr) => new(ptr);
    }
}
