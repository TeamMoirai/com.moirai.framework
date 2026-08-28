using System;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 加载操作状态，用于跟踪异步加载的去重和等待（后端无关：原始句柄以后端对象形式存放，由具体后端模式匹配取用）。
    /// </summary>
    internal sealed class LoadingOperationState : MemoryObject
    {
        private object _assetHandle;
        private object _subAssetsHandle;
        private bool _isDone;
        private bool _succeeded;
        private int _waiterCount;
        private bool _releaseRequested;

        /// <summary>
        /// 后端原始资源句柄（由具体资源后端解释）。
        /// </summary>
        public object AssetHandle
        {
            get => _assetHandle;
            set => _assetHandle = value;
        }

        /// <summary>
        /// 后端原子资源集句柄（由具体资源后端解释）。
        /// </summary>
        public object SubAssetsHandle
        {
            get => _subAssetsHandle;
            set => _subAssetsHandle = value;
        }

        /// <summary>
        /// 是否完成。
        /// </summary>
        public bool IsDone => _isDone;

        /// <summary>
        /// 是否成功。
        /// </summary>
        public bool Succeeded => _succeeded;

        /// <summary>
        /// 等待者数量。
        /// </summary>
        public int WaiterCount => _waiterCount;

        /// <summary>
        /// 是否已请求释放。
        /// </summary>
        public bool ReleaseRequested => _releaseRequested;

        /// <summary>
        /// 添加等待者。
        /// </summary>
        public void AddWaiter()
        {
            _waiterCount++;
        }

        /// <summary>
        /// 移除等待者。
        /// </summary>
        public void RemoveWaiter()
        {
            if (_waiterCount > 0)
            {
                _waiterCount--;
            }
        }

        /// <summary>
        /// 完成加载。
        /// </summary>
        /// <param name="success">是否成功。</param>
        public void Complete(bool success)
        {
            _isDone = true;
            _succeeded = success;
        }

        /// <summary>
        /// 请求释放。
        /// </summary>
        public void RequestRelease()
        {
            _releaseRequested = true;
        }

        /// <inheritdoc />
        public override void Clear()
        {
            _assetHandle = null;
            _subAssetsHandle = null;
            _isDone = false;
            _succeeded = false;
            _waiterCount = 0;
            _releaseRequested = false;
        }
    }
}
