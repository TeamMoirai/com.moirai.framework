using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Timer
{
    public delegate void TimerHandler(object[] args);

    // ReSharper disable once ClassNeverInstantiated.Global
    internal class TimerModule : Module, IUpdateModule, ITimerModule
    {
        [Serializable]
        internal class Timer
        {
            public int timerId = 0;
            public float curTime = 0;
            public float time = 0;
            public TimerHandler Handler;
            public bool isLoop = false;
            public bool isNeedRemove = false;
            public bool isRunning = false;
            public bool isUnscaled = false; // 是否使用非缩放的时间
            public object[] Args = null; // 回调参数
        }

        private int _curTimerId = 0;
        private readonly List<Timer> _timerList = new List<Timer>();
        private readonly List<Timer> _unscaledTimerList = new List<Timer>();
        private readonly List<int> _cacheRemoveTimers = new List<int>();
        private readonly List<int> _cacheRemoveUnscaledTimers = new List<int>();
        
        public override void OnInit() { }

        public override void Shutdown()
        {
            RemoveAllTimer();
            DestroySystemTimer();
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            UpdateTimer(elapseSeconds);
            UpdateUnscaledTimer(realElapseSeconds);
        }
        
        public int AddTimer(TimerHandler callback, float time, bool isLoop = false, bool isUnscaled = false, params object[] args)
        {
            Timer timer = new Timer
            {
                timerId = ++_curTimerId,
                curTime = time,
                time = time,
                Handler = callback,
                isLoop = isLoop,
                isUnscaled = isUnscaled,
                Args = args,
                isNeedRemove = false,
                isRunning = true
            };

            InsertTimer(timer);
            return timer.timerId;
        }

        private void InsertTimer(Timer timer)
        {
            bool isInsert = false;
            if (timer.isUnscaled)
            {
                for (int i = 0, len = _unscaledTimerList.Count; i < len; i++)
                {
                    if (_unscaledTimerList[i].curTime > timer.curTime)
                    {
                        _unscaledTimerList.Insert(i, timer);
                        isInsert = true;
                        break;
                    }
                }

                if (!isInsert)
                {
                    _unscaledTimerList.Add(timer);
                }
            }
            else
            {
                for (int i = 0, len = _timerList.Count; i < len; i++)
                {
                    if (_timerList[i].curTime > timer.curTime)
                    {
                        _timerList.Insert(i, timer);
                        isInsert = true;
                        break;
                    }
                }

                if (!isInsert)
                {
                    _timerList.Add(timer);
                }
            }
        }
        
        public void Stop(int timerId)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null) timer.isRunning = false;
        }
        
        public void Resume(int timerId)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null) timer.isRunning = true;
        }
        
        public bool IsRunning(int timerId)
        {
            Timer timer = GetTimer(timerId);
            return timer is { isRunning: true };
        }
        
        public float GetLeftTime(int timerId)
        {
            Timer timer = GetTimer(timerId);
            if (timer == null) return 0;
            return timer.curTime;
        }
        
        public void Restart(int timerId)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null)
            {
                timer.curTime = timer.time;
                timer.isRunning = true;
            }
        }

        public void ResetTimer(int timerId, TimerHandler callback, float time, bool isLoop = false, bool isUnscaled = false)
        {
            Reset(timerId, callback, time, isLoop, isUnscaled);
        }

        public void ResetTimer(int timerId, float time, bool isLoop, bool isUnscaled)
        {
            Reset(timerId, time, isLoop, isUnscaled);
        }
        
        public void Reset(int timerId, TimerHandler callback, float time, bool isLoop = false, bool isUnscaled = false)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null)
            {
                timer.curTime = time;
                timer.time = time;
                timer.Handler = callback;
                timer.isLoop = isLoop;
                timer.isNeedRemove = false;
                if (timer.isUnscaled != isUnscaled)
                {
                    RemoveTimerImmediate(timerId);

                    timer.isUnscaled = isUnscaled;
                    InsertTimer(timer);
                }
            }
        }
        
        public void Reset(int timerId, float time, bool isLoop, bool isUnscaled)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null)
            {
                timer.curTime = time;
                timer.time = time;
                timer.isLoop = isLoop;
                timer.isNeedRemove = false;
                if (timer.isUnscaled != isUnscaled)
                {
                    RemoveTimerImmediate(timerId);

                    timer.isUnscaled = isUnscaled;
                    InsertTimer(timer);
                }
            }
        }
        
        private void RemoveTimerImmediate(int timerId)
        {
            for (int i = 0, len = _timerList.Count; i < len; i++)
            {
                if (_timerList[i].timerId == timerId)
                {
                    _timerList.RemoveAt(i);
                    return;
                }
            }

            for (int i = 0, len = _unscaledTimerList.Count; i < len; i++)
            {
                if (_unscaledTimerList[i].timerId == timerId)
                {
                    _unscaledTimerList.RemoveAt(i);
                    return;
                }
            }
        }
        
        public void RemoveTimer(int timerId)
        {
            for (int i = 0, len = _timerList.Count; i < len; i++)
            {
                if (_timerList[i].timerId == timerId)
                {
                    _timerList[i].isNeedRemove = true;
                    return;
                }
            }

            for (int i = 0, len = _unscaledTimerList.Count; i < len; i++)
            {
                if (_unscaledTimerList[i].timerId == timerId)
                {
                    _unscaledTimerList[i].isNeedRemove = true;
                    return;
                }
            }
        }

        public void RemoveAllTimer()
        {
            _timerList.Clear();
            _unscaledTimerList.Clear();
        }

        private Timer GetTimer(int timerId)
        {
            for (int i = 0, len = _timerList.Count; i < len; i++)
            {
                if (_timerList[i].timerId == timerId)
                {
                    return _timerList[i];
                }
            }

            for (int i = 0, len = _unscaledTimerList.Count; i < len; i++)
            {
                if (_unscaledTimerList[i].timerId == timerId)
                {
                    return _unscaledTimerList[i];
                }
            }

            return null;
        }

        private void LoopCallInBadFrame()
        {
            bool isLoopCall = false;
            for (int i = 0, len = _timerList.Count; i < len; i++)
            {
                Timer timer = _timerList[i];
                if (timer.isLoop && timer.curTime <= 0)
                {
                    if (timer.Handler != null)
                    {
                        timer.Handler(timer.Args);
                    }

                    timer.curTime += timer.time;
                    if (timer.curTime <= 0)
                    {
                        isLoopCall = true;
                    }
                }
            }

            if (isLoopCall)
            {
                LoopCallInBadFrame();
            }
        }

        private void LoopCallUnscaledInBadFrame()
        {
            bool isLoopCall = false;
            for (int i = 0, len = _unscaledTimerList.Count; i < len; i++)
            {
                Timer timer = _unscaledTimerList[i];
                if (timer.isLoop && timer.curTime <= 0)
                {
                    if (timer.Handler != null)
                    {
                        timer.Handler(timer.Args);
                    }

                    timer.curTime += timer.time;
                    if (timer.curTime <= 0)
                    {
                        isLoopCall = true;
                    }
                }
            }

            if (isLoopCall)
            {
                LoopCallUnscaledInBadFrame();
            }
        }

        private void UpdateTimer(float elapseSeconds)
        {
            bool isLoopCall = false;
            for (int i = 0, len = _timerList.Count; i < len; i++)
            {
                Timer timer = _timerList[i];
                if (timer.isNeedRemove)
                {
                    _cacheRemoveTimers.Add(i);
                    continue;
                }

                if (!timer.isRunning) continue;
                timer.curTime -= elapseSeconds;
                if (timer.curTime <= 0)
                {
                    if (timer.Handler != null)
                    {
                        timer.Handler(timer.Args);
                    }

                    if (timer.isLoop)
                    {
                        timer.curTime += timer.time;
                        if (timer.curTime <= 0)
                        {
                            isLoopCall = true;
                        }
                    }
                    else
                    {
                        _cacheRemoveTimers.Add(i);
                    }
                }
            }

            for (int i = _cacheRemoveTimers.Count - 1; i >= 0; i--)
            {
                _timerList.RemoveAt(_cacheRemoveTimers[i]);
                _cacheRemoveTimers.RemoveAt(i);
            }

            if (isLoopCall)
            {
                LoopCallInBadFrame();
            }
        }

        private void UpdateUnscaledTimer(float realElapseSeconds)
        {
            bool isLoopCall = false;
            for (int i = 0, len = _unscaledTimerList.Count; i < len; i++)
            {
                Timer timer = _unscaledTimerList[i];
                if (timer.isNeedRemove)
                {
                    _cacheRemoveUnscaledTimers.Add(i);
                    continue;
                }

                if (!timer.isRunning) continue;
                timer.curTime -= realElapseSeconds;
                if (timer.curTime <= 0)
                {
                    if (timer.Handler != null)
                    {
                        timer.Handler(timer.Args);
                    }

                    if (timer.isLoop)
                    {
                        timer.curTime += timer.time;
                        if (timer.curTime <= 0)
                        {
                            isLoopCall = true;
                        }
                    }
                    else
                    {
                        _cacheRemoveUnscaledTimers.Add(i);
                    }
                }
            }

            for (int i = _cacheRemoveUnscaledTimers.Count - 1; i >= 0; i--)
            {
                _unscaledTimerList.RemoveAt(_cacheRemoveUnscaledTimers[i]);
                _cacheRemoveUnscaledTimers.RemoveAt(i);
            }

            if (isLoopCall)
            {
                LoopCallUnscaledInBadFrame();
            }
        }

        private readonly List<System.Timers.Timer> _ticker = new List<System.Timers.Timer>();

        public System.Timers.Timer AddSystemTimer(Action<object, System.Timers.ElapsedEventArgs> callBack)
        {
            int interval = 1000;
            var timerTick = new System.Timers.Timer(interval);
            timerTick.AutoReset = true;
            timerTick.Enabled = true;
            timerTick.Elapsed += new System.Timers.ElapsedEventHandler(callBack);

            _ticker.Add(timerTick);

            return timerTick;
        }

        private void DestroySystemTimer()
        {
            foreach (var ticker in _ticker)
            {
                if (ticker != null)
                {
                    ticker.Stop();
                }
            }
        }
    }
}