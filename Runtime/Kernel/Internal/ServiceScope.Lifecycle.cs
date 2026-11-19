using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 生命周期终态：严格逆序关闭（Shutdown）与资源销毁（Dispose）。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="ServiceScope"/>——公共 API 零变化（文件级单一职责）。</remarks>
    internal sealed partial class ServiceScope
    {

        private bool _isDisposing;

        /// <summary>
        /// 关闭驱动的唯一入口：容器先发 <see cref="IServiceInterceptor.OnServiceShutdown"/>（横切归容器）， <br />
        /// 再交 <see cref="IServiceLifecycle.Destroy"/> 走状态转换。
        /// </summary>
        /// <remarks>
        /// 前置状态判定只挡住常规重复调用；拦截器在回调里同步再关同一服务时事件会重发一次，但 <c>Destroy</c> 自身的幂等守卫保证 <c>OnShutdown</c> 仍只执行一次。
        /// </remarks>
        private void DestroyService(IService service)
        {
            if (GameServices.GetState(service) >= EServiceState.ShuttingDown) return;

            _world.InvokeShutdown(service);
            ((IServiceLifecycle)service).Destroy();
        }

        /// <summary>
        /// 关闭单个服务：生命周期驱动统一走 <see cref="DestroyService"/>，本方法仅负责注册表清理。
        /// </summary>
        private void ShutdownService(IService service)
        {
            if (!_entriesByService.TryGetValue(service, out var entry)) return;

            DestroyService(service);

            // 整体销毁时跳过逐项列表移除（由 DisposeInternal 统一 Clear），
            // 但注册表和 entries 必须逐项清理——否则作用域关闭后仍能解析到已关闭的服务
            if (_isDisposing)
            {
                for (int i = 0; i < entry.ContractHandles.Length; i++)
                {
                    _servicesByContract.Remove(entry.ContractHandles[i]);
                    _world.RemoveBinding(this, entry.ContractHandles[i], service);
                }
                _entriesByService.Remove(service);
                _world.InvokeUnregistered(service);
            }
            else
            {
                RemoveServiceInternal(service, entry);
            }
        }

        private void RemoveServiceInternal(IService service, ServiceEntry entry)
        {
            for (int i = 0; i < entry.ContractHandles.Length; i++)
            {
                _servicesByContract.Remove(entry.ContractHandles[i]);
                _world.RemoveBinding(this, entry.ContractHandles[i], service);
            }

            // _registrationOrder 必须保持注册序——使用 List.Remove（O(n) 移位保序），
            // 不用 swap-with-last（会破坏依赖方的关闭顺序保证）。
            // _activationOrder 同理移除——注销的服务不得残留在关闭序列中。
            _registrationOrder.Remove(service);
            _activationOrder.Remove(service);

            // 轮询列表使用 swap-with-last O(1) 移除 + 置脏标记，下次迭代前 lazy-sort。
            if (entry.TickIndex != MISSING_INDEX) RemoveTickableAt(entry.TickIndex);
            if (entry.FixedTickIndex != MISSING_INDEX) RemoveFixedTickableAt(entry.FixedTickIndex);
            if (entry.LateTickIndex != MISSING_INDEX) RemoveLateTickableAt(entry.LateTickIndex);
            if (entry.GizmoIndex != MISSING_INDEX) RemoveGizmoDrawableAt(entry.GizmoIndex);

            _entriesByService.Remove(service);
            _world.InvokeUnregistered(service);
        }


        public void Dispose()
        {
            if (IsDisposed) return;

            if (_isIterating)
            {
                _disposePending = true;
                return;
            }

            DisposeInternal();
        }

        private void DisposeInternal()
        {
            if (IsDisposed) return;
            PrepareDisposal();

            // 逆激活序（= 逆初始化序）关闭：依赖方（后初始化）先关闭，被依赖方后关闭。
            // 初始化顺序由世界拓扑排序决定、与注册顺序无关——逆注册序不保证满足
            // IService.OnShutdown 的"严格逆初始化序"契约（IService.cs），故依据激活记录执行。
            for (int i = _activationOrder.Count - 1; i >= 0; i--)
            {
                var service = _activationOrder[i];
                if (service != null && _entriesByService.ContainsKey(service))
                    ShutdownService(service);
            }

            // 未激活服务（世界未完成初始化即销毁；或 OnInit 抛异常未完成激活）：
            // 从未 OnInit，无初始化序可逆——保持既有兜底语义，仍驱动 OnShutdown，
            // 相对顺序沿用旧实现的逆注册序。
            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                if (service != null && _entriesByService.ContainsKey(service))
                    ShutdownService(service);
            }

            CompleteDisposal();
        }

        /// <summary>
        /// 销毁前置状态复位（同步/异步销毁共用）。
        /// </summary>
        private void PrepareDisposal()
        {
            _isIterating = false;
            _disposePending = false;
            _pendingChanges.Clear();

            _isDisposing = true;
        }

        /// <summary>
        /// 销毁收尾（同步/异步销毁共用）。
        /// </summary>
        private void CompleteDisposal()
        {
            _isDisposing = false;

            _registrationOrder.Clear();
            _activationOrder.Clear();
            _tickables.Clear();
            _fixedTickables.Clear();
            _lateTickables.Clear();
            _gizmoDrawables.Clear();
            _tickablesDirty = false;
            _fixedTickablesDirty = false;
            _lateTickablesDirty = false;
            _gizmoDrawablesDirty = false;
            _entriesByService.Clear();
            _servicesByContract.Clear();
            IsDisposed = true;
        }

        /// <summary>
        /// 异步销毁作用域：对实现 <see cref="IAsyncShutdownService"/> 的服务先调用 <c>OnShutdownAsync</c>，再调用同步 <c>OnShutdown</c>。
        /// </summary>
        /// <remarks>
        /// 按逆激活序（= 逆初始化序）执行；未激活服务归入兜底桶按逆注册序关闭（与 <see cref="DisposeInternal"/> 同语义）。
        /// </remarks>
        internal async UniTask DisposeAsync()
        {
            if (IsDisposed) return;

            if (_isIterating)
            {
                _disposePending = true;
                return;
            }

            PrepareDisposal();

            // 已激活服务：逆激活序，先异步关闭再同步关闭
            for (int i = _activationOrder.Count - 1; i >= 0; i--)
            {
                var service = _activationOrder[i];
                if (service == null || !_entriesByService.ContainsKey(service)) continue;

                if (service is IAsyncShutdownService asyncSvc)
                {
                    try { await asyncSvc.OnShutdownAsync(); }
                    catch (Exception ex)
                    {
                        LogUtility.Error("Service '{0}' OnShutdownAsync failed:\n{1}",
                            service.GetType().FullName, ex);
                    }
                }

                ShutdownService(service);
            }

            // 未激活服务：无初始化序可逆，保持既有兜底语义按逆注册序关闭
            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                if (service == null || !_entriesByService.ContainsKey(service)) continue;

                if (service is IAsyncShutdownService asyncSvc)
                {
                    try { await asyncSvc.OnShutdownAsync(); }
                    catch (Exception ex)
                    {
                        LogUtility.Error("Service '{0}' OnShutdownAsync failed:\n{1}",
                            service.GetType().FullName, ex);
                    }
                }

                ShutdownService(service);
            }

            CompleteDisposal();
        }
    }
}
