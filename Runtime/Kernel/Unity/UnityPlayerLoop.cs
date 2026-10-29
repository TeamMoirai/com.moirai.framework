using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Kernel.Scheduling;
using UnityEngine.LowLevel;
using KernelLogger = Moirai.Atropos.Kernel.Logging.ILogger;

namespace Moirai.Atropos.Kernel.Unity
{
    public sealed class UnityPlayerLoop : IPlayerLoop, IAsyncInitializable, IAsyncStoppable, IDisposable
    {
        private readonly CallbackRegistry _update;
        private readonly CallbackRegistry _fixedUpdate;
        private readonly CallbackRegistry _lateUpdate;
        private readonly PlayerLoopSystem.UpdateFunction _updateCallback;
        private readonly PlayerLoopSystem.UpdateFunction _fixedCallback;
        private readonly PlayerLoopSystem.UpdateFunction _lateCallback;
        private bool _initialized;
        private bool _disposed;

        public UnityPlayerLoop(KernelLogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _update = new CallbackRegistry(failure =>
                logger.Log(Logging.LogLevel.Error, nameof(UnityPlayerLoop), "An update listener failed.", failure));
            _fixedUpdate = new CallbackRegistry(failure =>
                logger.Log(Logging.LogLevel.Error, nameof(UnityPlayerLoop), "A fixed update listener failed.", failure));
            _lateUpdate = new CallbackRegistry(failure =>
                logger.Log(Logging.LogLevel.Error, nameof(UnityPlayerLoop), "A late update listener failed.", failure));
            _updateCallback = _update.Invoke;
            _fixedCallback = _fixedUpdate.Invoke;
            _lateCallback = _lateUpdate.Invoke;
        }

        public IDisposable AddUpdateListener(Action listener) => _update.Add(listener);
        public IDisposable AddFixedUpdateListener(Action listener) => _fixedUpdate.Add(listener);
        public IDisposable AddLateUpdateListener(Action listener) => _lateUpdate.Add(listener);

        public UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new ObjectDisposedException(nameof(UnityPlayerLoop));
            if (_initialized) return UniTask.CompletedTask;
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            Insert(ref loop, typeof(UnityEngine.PlayerLoop.Update), typeof(MoiraiKernelUpdate), _updateCallback);
            Insert(ref loop, typeof(UnityEngine.PlayerLoop.FixedUpdate), typeof(MoiraiKernelFixedUpdate), _fixedCallback);
            Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate), typeof(MoiraiKernelLateUpdate), _lateCallback);
            PlayerLoop.SetPlayerLoop(loop);
            _initialized = true;
            return UniTask.CompletedTask;
        }

        public UniTask StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_initialized)
            {
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                Remove(ref loop, typeof(MoiraiKernelUpdate), _updateCallback);
                Remove(ref loop, typeof(MoiraiKernelFixedUpdate), _fixedCallback);
                Remove(ref loop, typeof(MoiraiKernelLateUpdate), _lateCallback);
                PlayerLoop.SetPlayerLoop(loop);
                _initialized = false;
            }
            _update.Dispose();
            _fixedUpdate.Dispose();
            _lateUpdate.Dispose();
        }

        private static void Insert(ref PlayerLoopSystem loop, Type phase, Type marker,
            PlayerLoopSystem.UpdateFunction callback)
        {
            PlayerLoopSystem[] phases = loop.subSystemList;
            if (phases == null) throw new InvalidOperationException("The Unity player loop has no phases.");
            for (int i = 0; i < phases.Length; i++)
            {
                if (phases[i].type != phase) continue;
                PlayerLoopSystem[] existing = phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                for (int j = 0; j < existing.Length; j++)
                {
                    if (existing[j].type == marker && ReferenceEquals(existing[j].updateDelegate, callback)) return;
                }
                var systems = new PlayerLoopSystem[existing.Length + 1];
                Array.Copy(existing, systems, existing.Length);
                systems[existing.Length] = new PlayerLoopSystem { type = marker, updateDelegate = callback };
                phases[i].subSystemList = systems;
                return;
            }
            throw new InvalidOperationException($"Unity player loop phase {phase} is unavailable.");
        }

        private static void Remove(ref PlayerLoopSystem loop, Type marker, PlayerLoopSystem.UpdateFunction callback)
        {
            PlayerLoopSystem[] phases = loop.subSystemList;
            if (phases == null) return;
            for (int i = 0; i < phases.Length; i++)
            {
                PlayerLoopSystem[] systems = phases[i].subSystemList;
                if (systems == null) continue;
                int write = 0;
                for (int read = 0; read < systems.Length; read++)
                {
                    bool owned = systems[read].type == marker &&
                        ReferenceEquals(systems[read].updateDelegate, callback);
                    if (!owned) systems[write++] = systems[read];
                }
                if (write == systems.Length) continue;
                if (write == 0) phases[i].subSystemList = null;
                else
                {
                    Array.Resize(ref systems, write);
                    phases[i].subSystemList = systems;
                }
            }
        }

        private struct MoiraiKernelUpdate { }
        private struct MoiraiKernelFixedUpdate { }
        private struct MoiraiKernelLateUpdate { }
    }
}
