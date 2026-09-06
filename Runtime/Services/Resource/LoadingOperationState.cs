namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 加载操作状态，用于跟踪异步加载的去重和等待（后端无关：原始句柄以后端对象形式存放，由具体后端模式匹配取用）。
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
        }
    }
}
