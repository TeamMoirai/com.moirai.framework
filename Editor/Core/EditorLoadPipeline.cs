using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 编辑器窗口数据装载管线：即时步、主线程分帧切片步、后台任务步顺序执行，带取消、进度计数与阶段标签。
    /// </summary>
    /// <remarks>
    /// 起跑经编辑器 update 延一拍（自摘除 kick），保证调用方（如 OnEnable）先返回、窗口先绘制；切片步由编辑器 update
    /// 每拍在预算毫秒内连吃条目，跨拍分摊避免单帧长停顿；后台步经 <c>Task</c> 执行、续体由 Unity 主线程
    /// 同步上下文收口后再走下一步。装载中可在主线程步内继续登记后续步（发现步产出分组步的形态）。
    /// </remarks>
    public sealed class EditorLoadPipeline
    {

        #region 类型 [TYPES]

        private enum EStepKind
        {
            Immediate = 0,
            Sliced = 1,
            Background = 2
        }

        private sealed class Step
        {
            public EStepKind Kind;
            public string Stage;
            public Action Work;
            public Action<CancellationToken> BackgroundWork;
            public Action<CancellationToken> ProcessSlice;
            public Func<bool> IsSliceDone;
            public Action OnCompleted;
            public int Weight;
        }

        #endregion

        #region 字段 [FIELDS]

        private const float DEFAULT_SLICE_BUDGET_MS = 6f;

        private readonly List<Step> _steps = new List<Step>();
        private readonly Action _onComplete;
        private readonly Action<Exception> _onFail;
        private readonly Action _repaint;
        private readonly Func<bool> _isAlive;

        private CancellationTokenSource _cts;
        private int _index;
        private bool _started;
        private bool _running;
        private bool _failed;

        // 切片条目计数与进度权重：装载提示直接消费 ProcessedCount/TotalCount
        private int _processedCount;
        private int _totalCount;
        private float _weightDone;
        private float _weightTotal;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>装载是否在途。</summary>
        public bool IsRunning => _running;

        /// <summary>最近一次装载是否失败。</summary>
        public bool IsFailed => _failed;

        /// <summary>当前阶段标签。</summary>
        public string StageLabel { get; private set; } = string.Empty;

        /// <summary>已处理切片条目累计。</summary>
        public int ProcessedCount => _processedCount;

        /// <summary>已登记切片条目总数（装载中登记随之增长）。</summary>
        public int TotalCount => _totalCount;

        /// <summary>装载进度 0..1（即时/后台步权重 1，切片步权重为条目数）。</summary>
        public float Progress => _weightTotal <= 0f ? 1f : _weightDone / _weightTotal;

        #endregion

        #region 构造与登记 [CONSTRUCTION & REGISTRATION]

        /// <summary>
        /// 构造装载管线。
        /// </summary>
        /// <param name="onComplete">全部步骤完成回调（主线程）。</param>
        /// <param name="onFail">装载失败回调（主线程，异常已由管线落 Console）。</param>
        /// <param name="repaint">切片每拍回调（驱动装载提示重绘），可空。</param>
        /// <param name="isAlive">宿主存活判定（窗口销毁即停），可空。</param>
        public EditorLoadPipeline(Action onComplete, Action<Exception> onFail, Action repaint = null, Func<bool> isAlive = null)
        {
            _onComplete = onComplete;
            _onFail = onFail;
            _repaint = repaint;
            _isAlive = isAlive;
        }

        /// <summary>
        /// 登记主线程即时步（同拍连跑，仅限轻活）。
        /// </summary>
        public void AddStep(string stage, Action work, Action onCompleted = null)
        {
            Add(new Step { Kind = EStepKind.Immediate, Stage = stage, Work = work, OnCompleted = onCompleted, Weight = 1 });
        }

        /// <summary>
        /// 登记主线程分帧切片步：每编辑器拍在预算毫秒内处理条目，跨拍分摊避免单帧长停顿；空批次立即完成。
        /// </summary>
        public void AddSlicedStep<T>(string stage, IReadOnlyList<T> items, Action<T> process,
            float budgetMs = DEFAULT_SLICE_BUDGET_MS, Action onCompleted = null)
        {
            if (items == null || items.Count == 0)
            {
                Add(new Step { Kind = EStepKind.Immediate, Stage = stage, OnCompleted = onCompleted, Weight = 0 });
                return;
            }

            var list = items;
            var body = process;
            int cursor = 0;

            var step = new Step
            {
                Kind = EStepKind.Sliced,
                Stage = stage,
                OnCompleted = onCompleted,
                Weight = items.Count
            };
            step.ProcessSlice = token =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    if (token.IsCancellationRequested) return;
                    body(list[cursor]);
                    cursor++;
                    _processedCount++;
                    if (cursor >= list.Count || sw.ElapsedMilliseconds >= budgetMs) return;
                }
                while (true);
            };
            step.IsSliceDone = () => cursor >= list.Count;

            _totalCount += items.Count;
            Add(step);
        }

        /// <summary>
        /// 登记后台任务步：<c>Task</c> 执行、禁止触碰 Unity API，续体回主线程后再走下一步。
        /// </summary>
        public void AddBackgroundStep(string stage, Action<CancellationToken> work, Action onCompleted = null)
        {
            Add(new Step { Kind = EStepKind.Background, Stage = stage, BackgroundWork = work, OnCompleted = onCompleted, Weight = 1 });
        }

        private void Add(Step step)
        {
            _steps.Add(step);
            _weightTotal += step.Weight;
        }

        #endregion

        #region 执行 [EXECUTION]

        /// <summary>
        /// 起跑：经编辑器 update 自摘除延一拍执行，保证调用方先返回、界面先绘制。
        /// </summary>
        /// <remarks>
        /// 不走 <c>delayCall</c>——它只随真实编辑器循环派发，EditMode 测试的模拟泵不触发，起跑会悬停。
        /// </remarks>
        public void Start()
        {
            if (_started) return;
            _started = true;
            _running = true;
            _failed = false;
            _cts = new CancellationTokenSource();
            EditorApplication.update += Kick;
        }

        private void Kick()
        {
            EditorApplication.update -= Kick;
            Run();
        }

        /// <summary>取消装载（幂等）：停止切片与续体，不回调完成/失败。</summary>
        public void Cancel()
        {
            if (!_running) return;
            _running = false;
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async void Run()
        {
            if (!_running) return;
            if (_isAlive != null && !_isAlive())
            {
                _running = false;
                return;
            }

            try
            {
                CancellationToken token = _cts.Token;

                while (true)
                {
                    // 即时步同拍连跑；步体可继续登记后续步（发现步产出分组步的形态）
                    while (_index < _steps.Count && _steps[_index].Kind == EStepKind.Immediate)
                    {
                        token.ThrowIfCancellationRequested();
                        Step step = _steps[_index];
                        StageLabel = step.Stage;
                        step.Work?.Invoke();
                        _weightDone += step.Weight;
                        step.OnCompleted?.Invoke();
                        _index++;
                    }

                    if (_index >= _steps.Count) break;

                    Step current = _steps[_index];
                    token.ThrowIfCancellationRequested();
                    StageLabel = current.Stage;
                    if (current.Kind == EStepKind.Sliced)
                    {
                        await RunSlicedAsync(current, token);
                    }
                    else
                    {
                        await Task.Run(() => current.BackgroundWork(token), token);
                    }

                    token.ThrowIfCancellationRequested();
                    _weightDone += current.Weight;
                    current.OnCompleted?.Invoke();
                    _index++;
                }

                Complete();
            }
            catch (OperationCanceledException)
            {
                // 取消即静默收场：不回调完成/失败
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        /// <summary>
        /// 切片步执行：订阅编辑器 update，每拍吃预算内条目；完成或取消时退订并唤醒续体。
        /// </summary>
        private Task RunSlicedAsync(Step step, CancellationToken token)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (!_running || (_isAlive != null && !_isAlive()))
                {
                    _running = false;
                    _cts?.Cancel();
                    EditorApplication.update -= tick;
                    completion.TrySetResult(true);
                    return;
                }

                step.ProcessSlice(token);
                _repaint?.Invoke();

                if (step.IsSliceDone())
                {
                    EditorApplication.update -= tick;
                    completion.TrySetResult(true);
                }
            };

            EditorApplication.update += tick;
            if (step.IsSliceDone())
            {
                EditorApplication.update -= tick;
                completion.TrySetResult(true);
            }

            return completion.Task;
        }

        private void Complete()
        {
            _running = false;
            try
            {
                _onComplete?.Invoke();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private void Fail(Exception exception)
        {
            _running = false;
            _failed = true;
            Debug.LogException(exception);
            try
            {
                _onFail?.Invoke(exception);
            }
            catch (Exception secondary)
            {
                Debug.LogException(secondary);
            }
        }

        #endregion

    }
}
