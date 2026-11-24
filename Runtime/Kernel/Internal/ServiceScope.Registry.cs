using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务注册：入图、依赖声明、契约去重与注册期校验。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="ServiceScope"/>——公共 API 零变化（文件级单一职责）。</remarks>
    internal sealed partial class ServiceScope
    {

        /// <summary>
        /// 两阶段第一阶段：仅入注册表（契约映射 + 条目 + 注册序），不驱动生命周期、不加入轮询列表。
        /// </summary>
        /// <remarks>
        /// 世界初始化时经 <see cref="ActivateService"/> 补齐轮询列表并驱动 OnInit。
        /// </remarks>
        internal void RegisterDeferred(Type contractType, IService service, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            if (IsDisposed || _disposePending)
                throw new GameException(StringUtility.Format(
                    "Scope {0} is disposed or disposing; registration is rejected.", Kind));

            if (_servicesByContract.ContainsKey(contractType.TypeHandle))
                throw new GameException(StringUtility.Format(
                    "Contract '{0}' has already been registered in {1} scope.",
                    contractType.FullName, Kind));

            RegisterInternal(service, new[] { contractType }, activateTickables: false);
        }

        /// <summary>
        /// 两阶段第二阶段（逐服务）：补齐轮询列表并驱动 OnInit，由世界按拓扑序调用。
        /// </summary>
        /// <remarks>
        /// OnInit 成功完成后记录进激活序（<see cref="_activationOrder"/>）；初始化抛异常的服务不进入激活序，销毁时归入未激活桶兜底关闭。
        /// 初始化抛异常时轮询列表先行摘除再上抛——未完成 OnInit 的服务不得以未就绪状态被逐帧驱动；
        /// 注册表不摘：解析、关闭序与未激活桶兜底语义保持原样。
        /// <c>OnServiceRegistered</c> 在此按条目已挂载的<b>契约句柄</b>逐个发出（每契约一次），使 Registering/Registered 两个事件的契约参数成对。
        /// </remarks>
        internal void ActivateService(IService service)
        {
            ActivateTickables(service);

            try
            {
                ((IServiceLifecycle)service).Initialize();
            }
            catch
            {
                DemoteTickables(service);
                throw;
            }

            if (_entriesByService.TryGetValue(service, out var entry) && !entry.ActivationRecorded)
            {
                entry.ActivationRecorded = true;
                _entriesByService[service] = entry;
                _activationOrder.Add(service);

                var handles = entry.ContractHandles;
                for (int i = 0; i < handles.Length; i++)
                    _world.InvokeRegistered(service, Type.GetTypeFromHandle(handles[i]), Kind);
            }
        }

        /// <summary>
        /// 将服务从其已挂入的轮询列表逐类摘除（激活失败路径专用；条目保留、轮询索引复位）。
        /// </summary>
        private void DemoteTickables(IService service)
        {
            if (!_entriesByService.TryGetValue(service, out var entry)) return;

            if (entry.TickIndex != MISSING_INDEX) RemoveTickableAt(entry.TickIndex);
            entry.TickIndex = MISSING_INDEX;
            if (entry.FixedTickIndex != MISSING_INDEX) RemoveFixedTickableAt(entry.FixedTickIndex);
            entry.FixedTickIndex = MISSING_INDEX;
            if (entry.LateTickIndex != MISSING_INDEX) RemoveLateTickableAt(entry.LateTickIndex);
            entry.LateTickIndex = MISSING_INDEX;
            if (entry.GizmoIndex != MISSING_INDEX) RemoveGizmoDrawableAt(entry.GizmoIndex);
            entry.GizmoIndex = MISSING_INDEX;

            _entriesByService[service] = entry;
        }

        /// <summary>
        /// 待初始化阶段注销：从注册表移除（无生命周期、无事件——服务从未初始化）。
        /// </summary>
        internal bool UnregisterDeferred(Type contractType, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (IsDisposed) return false;
            if (!_servicesByContract.TryGetValue(contractType.TypeHandle, out var service)) return false;

            if (_entriesByService.TryGetValue(service, out var entry))
            {
                for (int i = 0; i < entry.ContractHandles.Length; i++)
                {
                    _servicesByContract.Remove(entry.ContractHandles[i]);
                    _world.RemoveBinding(this, entry.ContractHandles[i], service);
                }
                _registrationOrder.Remove(service);
                _entriesByService.Remove(service);
            }
            return true;
        }

        /// <summary>
        /// 运行时注册（世界已初始化）：立即驱动服务生命周期（OnInit）。
        /// </summary>
        /// <remarks>
        /// 迭代中（Tick）调用时默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）。
        /// </remarks>
        internal IService RegisterRuntime(Type contractType, IService service, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            if (IsDisposed)
                throw new GameException(StringUtility.Format(
                    "Scope {0} has been disposed; runtime registration is rejected.", Kind));

            if (_disposePending)
                throw new GameException(StringUtility.Format(
                    "Scope {0} is being disposed; runtime registration is rejected.", Kind));

            if (_servicesByContract.ContainsKey(contractType.TypeHandle))
                throw new GameException(StringUtility.Format(
                    "Contract '{0}' has already been registered in {1} scope.",
                    contractType.FullName, Kind));

            if (_isIterating)
            {
                if (deferMode == EDeferMode.Throw)
                    throw new GameException(StringUtility.Format(
                        "Cannot register '{0}' while {1} scope is iterating (EDeferMode.Throw).",
                        contractType.FullName, Kind));

                for (int i = 0; i < _pendingChanges.Count; i++)
                {
                    if (_pendingChanges[i].Kind != PendingChangeKind.Unregister &&
                        _pendingChanges[i].ContractType == contractType)
                        throw new GameException(StringUtility.Format(
                            "Contract '{0}' has a pending registration in {1} scope.",
                            contractType.FullName, Kind));
                }

                _pendingChanges.Add(PendingChange.ForRegister(service, contractType));
                return service;
            }

            RegisterInternal(service, new[] { contractType }, activateTickables: false);

            // 与世界初始化路径共用 ActivateService：补齐轮询列表 + 驱动 OnInit + 记录激活序
            ActivateService(service);

            return service;
        }

        /// <summary>
        /// 运行时注销并关闭单个服务（触发 OnShutdown 并从注册表移除）。
        /// </summary>
        internal bool UnregisterRuntime(Type serviceType, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (IsDisposed) return false;

            if (!_servicesByContract.ContainsKey(serviceType.TypeHandle))
                return false;

            if (_isIterating)
            {
                if (deferMode == EDeferMode.Throw)
                    throw new GameException(StringUtility.Format(
                        "Cannot unregister '{0}' while {1} scope is iterating (EDeferMode.Throw).",
                        serviceType.FullName, Kind));

                _pendingChanges.Add(PendingChange.ForUnregister(serviceType));
                return true;
            }

            var service = _servicesByContract[serviceType.TypeHandle];

            DestroyService(service);

            if (_entriesByService.TryGetValue(service, out var entry))
                RemoveServiceInternal(service, entry);

            return true;
        }

        /// <summary>
        /// 为已注册的服务实例附加一个新契约绑定（多契约支持）。
        /// </summary>
        internal void BindAdditionalContractRuntime(Type contractType, IService service, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (service == null) throw new ArgumentNullException(nameof(service));

            if (IsDisposed || _disposePending)
                throw new GameException(StringUtility.Format(
                    "Scope {0} is disposed or disposing; contract binding is rejected.", Kind));

            if (_servicesByContract.ContainsKey(contractType.TypeHandle))
                throw new GameException(StringUtility.Format(
                    "Contract '{0}' has already been registered in {1} scope.",
                    contractType.FullName, Kind));

            if (!_entriesByService.ContainsKey(service))
                throw new GameException(StringUtility.Format(
                    "Service '{0}' is not registered in {1} scope; register it before binding additional contracts.",
                    service.GetType().FullName, Kind));

            if (_isIterating)
            {
                if (deferMode == EDeferMode.Throw)
                    throw new GameException(StringUtility.Format(
                        "Cannot bind '{0}' while {1} scope is iterating (EDeferMode.Throw).",
                        contractType.FullName, Kind));

                for (int i = 0; i < _pendingChanges.Count; i++)
                {
                    if (_pendingChanges[i].Kind != PendingChangeKind.Unregister &&
                        _pendingChanges[i].ContractType == contractType)
                        throw new GameException(StringUtility.Format(
                            "Contract '{0}' has a pending registration in {1} scope.",
                            contractType.FullName, Kind));
                }

                _pendingChanges.Add(PendingChange.ForBind(service, contractType));
                return;
            }

            _world.InvokeRegistering(service, contractType, Kind);
            AttachContractCore(service, contractType);

            // 待初始化阶段的附加契约：同步挂起图（拓扑边解析需要全部契约）；
            // 世界已初始化的运行时绑定不进入挂起图
            if (!_world.IsInitialized)
                _world.TrackPendingContract(contractType, service);
        }

        /// <summary>
        /// 附加契约句柄到既有条目（立即路径与延迟 flush 共用）。
        /// </summary>
        /// <remarks>
        /// 新契约的 <c>OnServiceRegistered</c> 仅在服务已激活时立即发出；待初始化阶段的附加契约推迟到 <see cref="ActivateService"/> 按全部句柄统一发， <br />
        /// 以保持"Registered 时 OnInit 必已执行"的事件契约。
        /// </remarks>
        private void AttachContractCore(IService service, Type contractType)
        {
            var entry = _entriesByService[service];

            var oldHandles = entry.ContractHandles;
            var newHandles = new RuntimeTypeHandle[oldHandles.Length + 1];
            Array.Copy(oldHandles, newHandles, oldHandles.Length);
            newHandles[oldHandles.Length] = contractType.TypeHandle;
            entry.ContractHandles = newHandles;

            _servicesByContract[newHandles[oldHandles.Length]] = service;
            _world.AddBinding(this, newHandles[oldHandles.Length], service);

            _entriesByService[service] = entry;

            if (entry.ActivationRecorded)
                _world.InvokeRegistered(service, contractType, Kind);
        }

        private void RegisterInternal(IService service, Type[] contractTypes, bool activateTickables)
        {
            // MonoBehaviour 服务的 Tick 应由 Unity 生命周期驱动，不可混入 ServiceScope 轮询列表
            if (service is MonoBehaviour)
            {
                if (service is IServiceTickable)
                    throw new GameException(StringUtility.Format(
                        "MonoBehaviour service '{0}' cannot implement IServiceTickable. " +
                        "Use Unity's Update() instead.", service.GetType().FullName));
                if (service is IServiceFixedTickable)
                    throw new GameException(StringUtility.Format(
                        "MonoBehaviour service '{0}' cannot implement IServiceFixedTickable. " +
                        "Use Unity's FixedUpdate() instead.", service.GetType().FullName));
                if (service is IServiceLateTickable)
                    throw new GameException(StringUtility.Format(
                        "MonoBehaviour service '{0}' cannot implement IServiceLateTickable. " +
                        "Use Unity's LateUpdate() instead.", service.GetType().FullName));
                // Gizmo 同理：Unity 对任意 MonoBehaviour 的 OnDrawGizmos 消息无条件反射调用，
                // 再进本容器的轮询列表就是编辑器下双重绘制
                if (service is IServiceGizmoDrawable)
                    throw new GameException(StringUtility.Format(
                        "MonoBehaviour service '{0}' cannot implement IServiceGizmoDrawable. " +
                        "Unity already drives OnDrawGizmos() on the component; the container would draw it twice.",
                        service.GetType().FullName));
            }

            // 否决通道：Registering 必须在任何注册表写入之前发——抛出后不留半注册状态，
            // 与附加契约路径（BindAdditionalContractRuntime 先通知后 Attach）保持同一顺序。
            _world.InvokeRegistering(service, contractTypes[0], Kind);

            var handles = new RuntimeTypeHandle[contractTypes.Length];
            for (int i = 0; i < contractTypes.Length; i++)
            {
                handles[i] = contractTypes[i].TypeHandle;
                _servicesByContract[handles[i]] = service;
                _world.AddBinding(this, handles[i], service);
            }

            var entry = new ServiceEntry
            {
                ContractHandles = handles,
                CreationIndex = _nextCreationIndex++,
                TickIndex = MISSING_INDEX,
                FixedTickIndex = MISSING_INDEX,
                LateTickIndex = MISSING_INDEX,
                GizmoIndex = MISSING_INDEX,
            };

            _registrationOrder.Add(service);
            _entriesByService[service] = entry;

            if (activateTickables)
                ActivateTickables(service);
        }

        /// <summary>
        /// 将服务加入其能力接口对应的轮询列表（幂等——已有索引即跳过）。
        /// </summary>
        private void ActivateTickables(IService service)
        {
            if (!_entriesByService.TryGetValue(service, out var entry)) return;

            if (service is IServiceTickable tickable && entry.TickIndex == MISSING_INDEX)
            {
                entry.TickIndex = _tickables.Count;
                _tickables.Add(tickable);
                _tickablesDirty = true;
            }
            if (service is IServiceFixedTickable fixedTickable && entry.FixedTickIndex == MISSING_INDEX)
            {
                entry.FixedTickIndex = _fixedTickables.Count;
                _fixedTickables.Add(fixedTickable);
                _fixedTickablesDirty = true;
            }
            if (service is IServiceLateTickable lateTickable && entry.LateTickIndex == MISSING_INDEX)
            {
                entry.LateTickIndex = _lateTickables.Count;
                _lateTickables.Add(lateTickable);
                _lateTickablesDirty = true;
            }
            if (service is IServiceGizmoDrawable gizmo && entry.GizmoIndex == MISSING_INDEX)
            {
                entry.GizmoIndex = _gizmoDrawables.Count;
                _gizmoDrawables.Add(gizmo);
                _gizmoDrawablesDirty = true;
            }

            _entriesByService[service] = entry;
        }
    }
}
