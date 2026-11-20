using System;
using System.Collections.Generic;
using UnityEngine;
using Moirai.Atropos.Asset;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 惰性租约作用域：聚合多条 <see cref="AssetLeaseHandle"/>，Dispose 时统一释放——
    /// 使用者（流程状态/UI 页面等）持有本作用域即持有"资产所有权集合"，生命周期终点一次归还。
    /// </summary>
    /// <remarks>
    /// 惰性语义：构造零成本，首次 <see cref="Load{T}(string, string)"/> 才实际加载并登记租约。 <br />
    /// 依赖注入：框架内部经 <see cref="AssetLeaseScope(IAssetService)"/> 注入契约实例；公共无参构造保持兼容，
    /// 首次 <see cref="Load{T}(string, string)"/> 时经容器解析一次并缓存。 <br />
    /// 典型用法：流程状态/UI 页面挂本作用域，Leave 时自动 Dispose——
    /// 状态持有的资产生命周期 = 状态生命周期，消除手工逐租约释放的遗漏面。 <br />
    /// 线程契约：仅限 Unity 主线程（租约物化涉及 Unity 对象加载）。 <br />
    /// 释放顺序：登记的逆序归还（后取的资产可能引用先取的）。 <br />
    /// 重复 Dispose 安全：幂等，二次调用无副作用。
    /// </remarks>
    public sealed class AssetLeaseScope : IDisposable
    {
        /// <summary>已登记租约（登记序）。Dispose 后清空。</summary>
        private readonly List<AssetLeaseHandle> _leases = new List<AssetLeaseHandle>();

        /// <summary>资产契约实例：注入构造直接持有；无参构造首次 Load 时经容器解析一次并缓存。</summary>
        private IAssetService _assetService;

        /// <summary>是否已释放。</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>当前登记的租约数量（诊断用）。</summary>
        public int LeaseCount => _leases.Count;

        /// <summary>
        /// 注入构造：本作用域经注入的资产契约实例加载与归还（框架内部与测试用）。
        /// </summary>
        /// <param name="assetService">资产契约实例（Phase 2 注入互调）。</param>
        internal AssetLeaseScope(IAssetService assetService)
        {
            _assetService = assetService;
        }

        /// <summary>
        /// 惰性构造（公共 API 兼容）：首次 <see cref="Load{T}(string, string)"/> 时经容器解析资产契约并缓存。
        /// </summary>
        public AssetLeaseScope()
        {
        }

        /// <summary>
        /// 惰性同步加载并登记：资产租约归本作用域所有，Dispose 时统一归还。
        /// </summary>
        /// <typeparam name="T">资产类型。</typeparam>
        /// <param name="location">资产定位。</param>
        /// <param name="packageName">资源包名（空为默认包）。</param>
        public T Load<T>(string location, string packageName = "") where T : UObject
        {
            if (IsDisposed)
            {
                throw new GameException(
                    "AssetLeaseScope was disposed — Load is unavailable after Dispose. " +
                    "Ensure the owning state/page recreates the scope on re-enter.");
            }
            _assetService ??= GameServices.GetRequiredService<IAssetService>();
            var lease = _assetService.LoadLease<T>(location, packageName);
            _leases.Add(lease.Handle);
            return lease.Asset;
        }

        /// <summary>
        /// 释放全部登记租约（登记逆序归还），幂等。
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            for (int i = _leases.Count - 1; i >= 0; i--)
            {
                _assetService.Release(_leases[i]);
            }
            _leases.Clear();
        }
    }
}
