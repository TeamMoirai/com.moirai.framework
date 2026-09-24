namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 加载操作状态，用于跟踪异步加载的去重和等待（后端无关：原始句柄以后端对象形式存放，由具体后端模式匹配取用）。
    /// <para>完成源与状态同生共死：<see cref="Complete"/> 一次唤醒所有等待者，等待方不再
    /// <c>while (!IsDone) await UniTask.Yield()</c> 空转。源经 <see cref="Preserve"/> 允许多等待者，
    /// <see cref="Clear"/> 时整棵重置回池。</para>
    /// </summary>
    internal sealed class LoadingOperationState : MemoryObject
    {
        /// <summary>
        /// 后端原始资源句柄（由具体资源后端解释）。
        /// </summary>
        public object AssetHandle { get; set; }

        /// <summary>
        /// 后端原子资源集句柄（由具体资源后端解释）。
        /// </summary>
        public object SubAssetsHandle { get; set; }

        /// <summary>
        /// 是否完成。
        /// </summary>
        public bool IsDone { get; private set; }

        /// <summary>
        /// 是否成功。
        /// </summary>
        public bool Succeeded { get; private set; }

        /// <summary>
        /// 等待者数量。
        /// </summary>
        public int WaiterCount { get; private set; }

        /// <summary>
        /// 是否已请求释放。
        /// </summary>
        public bool ReleaseRequested { get; private set; }

        private Cysharp.Threading.Tasks.UniTaskCompletionSource<bool> _completion;
        private Cysharp.Threading.Tasks.UniTask<bool> _waitTask;

        /// <summary>
        /// 等待完成；已结束则同步给出结果。多等待者共享同一条 Preserve 任务。
        /// </summary>
        public Cysharp.Threading.Tasks.UniTask<bool> WaitAsync()
        {
            if (IsDone)
            {
                return Cysharp.Threading.Tasks.UniTask.FromResult(Succeeded);
            }

            if (_completion == null)
            {
                _completion = new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();
                // Preserve：UniTask 源默认单次 await，加载去重允许 N 个等待者挂在同一结果上。
                _waitTask = _completion.Task.Preserve();
            }

            return _waitTask;
        }

        /// <summary>
        /// 添加等待者。
        /// </summary>
        public void AddWaiter()
        {
            WaiterCount++;
        }

        /// <summary>
        /// 移除等待者。
        /// </summary>
        public void RemoveWaiter()
        {
            if (WaiterCount > 0)
            {
                WaiterCount--;
            }
        }

        /// <summary>
        /// 完成加载。
        /// </summary>
        /// <param name="success">是否成功。</param>
        public void Complete(bool success)
        {
            IsDone = true;
            Succeeded = success;
            _completion?.TrySetResult(success);
        }

        /// <summary>
        /// 请求释放。
        /// </summary>
        public void RequestRelease()
        {
            ReleaseRequested = true;
        }

        /// <inheritdoc />
        public override void Clear()
        {
            AssetHandle = null;
            SubAssetsHandle = null;
            IsDone = false;
            Succeeded = false;
            WaiterCount = 0;
            ReleaseRequested = false;
            _completion = null;
            _waitTask = default;
        }
    }
}
