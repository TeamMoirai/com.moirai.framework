using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源所有者组件，挂在需要绑定资源的 GameObject 上，OnDestroy 时自动释放所有绑定。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResourceOwner : MonoBehaviour
    {
        #region 常量 [CONSTANTS]

        private const int DefaultReleaseBufferCapacity = 64;

        #endregion

        #region 字段 [FIELDS]

        private static readonly List<ResourceOwner> s_ReleaseBuffer = new List<ResourceOwner>(DefaultReleaseBufferCapacity);
        private static int s_ReleaseBufferCapacity = DefaultReleaseBufferCapacity;

        private int _ownerId;
        private ulong _gameObjectId;
        private uint _generation;
        private bool _isRegistered;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 所有者 ID。
        /// </summary>
        public int OwnerId => _ownerId;

        /// <summary>
        /// GameObject ID。
        /// </summary>
        public ulong GameObjectId => _gameObjectId;

        /// <summary>
        /// 代际标记。
        /// </summary>
        public uint Generation => _generation;

        /// <summary>
        /// 是否已注册。
        /// </summary>
        public bool IsRegistered => _isRegistered;

        #endregion

        #region 内部方法 [INTERNAL METHODS]

        internal void SetRegistered(int newOwnerId, ulong newGameObjectId, uint newGeneration)
        {
            _ownerId = newOwnerId;
            _gameObjectId = newGameObjectId;
            _generation = newGeneration;
            _isRegistered = true;
        }

        internal void ClearRegistered()
        {
            _ownerId = 0;
            _gameObjectId = 0;
            _generation = 0;
            _isRegistered = false;
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 释放此所有者上的所有绑定。
        /// </summary>
        /// <returns>绑定结果状态。</returns>
        public EResourceBindStatus ReleaseBindings()
        {
            if (!_isRegistered)
            {
                return EResourceBindStatus.MissingOwner;
            }

            int currentOwnerId = _ownerId;
            uint currentGeneration = _generation;
            EResourceBindStatus status = EResourceBindStatus.ServiceShutdown;

            IResourceService resourceService = GameApp.Resource;
            if (resourceService != null)
            {
                IResourceBindingService bindingService = resourceService.BindingService;
                if (bindingService != null)
                {
                    status = bindingService.ReleaseOwner(currentOwnerId, currentGeneration);
                }
            }

            if (_isRegistered && _ownerId == currentOwnerId && _generation == currentGeneration)
            {
                ClearRegistered();
            }

            return status;
        }

        /// <summary>
        /// 释放层级中所有 ResourceOwner 的绑定。
        /// </summary>
        /// <param name="root">根 GameObject。</param>
        /// <returns>已释放的绑定数量。</returns>
        public static int ReleaseBindingsInHierarchy(GameObject root)
        {
            if (root == null)
            {
                return 0;
            }

            if (s_ReleaseBuffer.Capacity < s_ReleaseBufferCapacity)
            {
                s_ReleaseBuffer.Capacity = s_ReleaseBufferCapacity;
            }

            root.GetComponentsInChildren(true, s_ReleaseBuffer);

            int releasedCount = 0;
            for (int i = 0; i < s_ReleaseBuffer.Count; i++)
            {
                ResourceOwner owner = s_ReleaseBuffer[i];
                if (owner == null || !owner._isRegistered)
                {
                    continue;
                }

                owner.ReleaseBindings();
                releasedCount++;
            }

            s_ReleaseBuffer.Clear();
            return releasedCount;
        }

        /// <summary>
        /// 预热释放缓冲区容量。
        /// </summary>
        /// <param name="capacity">目标容量。</param>
        public static void WarmupReleaseBuffer(int capacity)
        {
            if (capacity <= s_ReleaseBufferCapacity)
            {
                return;
            }

            s_ReleaseBufferCapacity = capacity;
            s_ReleaseBuffer.Capacity = capacity;
        }

        /// <summary>
        /// 确保组件上有 ResourceOwner。
        /// </summary>
        /// <param name="target">目标组件。</param>
        /// <param name="bindingService">绑定服务。</param>
        /// <returns>ResourceOwner 实例。</returns>
        public static ResourceOwner EnsureFor(Component target, IResourceBindingService bindingService)
        {
            if (target == null || target.gameObject == null)
            {
                return null;
            }

            ResourceOwner owner = target.GetComponent<ResourceOwner>();
            if (owner == null)
            {
                owner = target.gameObject.AddComponent<ResourceOwner>();
            }

            bindingService?.RegisterOwner(owner);
            return owner;
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        private void OnDestroy()
        {
            if (!_isRegistered)
            {
                return;
            }

            ReleaseBindings();
        }

        #endregion
    }
}
