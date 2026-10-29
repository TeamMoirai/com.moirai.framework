using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition.Internal;

namespace Moirai.Atropos.Kernel.Composition
{
    public sealed class ServiceContainer
    {
        private RegistrationGraph _graph;
        private ServiceContainer _root;
        private readonly object[] _instances;
        private readonly bool[] _constructing;
        private HashSet<object> _owners;
        private readonly List<ServiceContainer> _scopes = new List<ServiceContainer>();
        private readonly UniTaskCompletionSource<Exception> _initialized = new UniTaskCompletionSource<Exception>();
        private readonly UniTaskCompletionSource _initializationEnded = new UniTaskCompletionSource();
        private readonly UniTaskCompletionSource<Exception> _stopped = new UniTaskCompletionSource<Exception>();
        private readonly List<Exception> _failures = new List<Exception>();
        private CancellationTokenSource _initialization;
        private CancellationToken _initialCaller;
        private UniTaskCompletionSource _constructionEnded;
        private int _constructionDepth;
        private bool _initializationStarted;
        private bool _closing;
        private Exception _initializationFailure;

        public ServiceContainer(IServiceModule[] modules)
        {
            _graph = new RegistrationGraph(modules);
            _root = this;
            _owners = new HashSet<object>(new InstanceComparer());
            _instances = new object[_graph.Nodes.Length];
            _constructing = new bool[_instances.Length];
        }

        private ServiceContainer(ServiceContainer root)
        {
            _graph = root._graph;
            _root = root;
            _owners = root._owners;
            _instances = new object[_graph.Nodes.Length];
            _constructing = new bool[_instances.Length];
        }

        internal bool IsReleased => _graph == null && _owners == null && _root == null;

        public T Get<T>() where T : class => (T)Get(typeof(T));

        public object Get(Type contract)
        {
            EnsureOpen();
            object instance = Resolve(_graph.IndexOf(contract));
            EnsureOpen();
            return instance;
        }

        public ServiceContainer CreateScope()
        {
            EnsureOpen();
            if (_root != this) throw new InvalidOperationException("Only the root container creates scopes.");
            var scope = new ServiceContainer(this);
            _scopes.Add(scope);
            return scope;
        }

        public UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            bool first = !_initializationStarted;
            if (first)
            {
                _initializationStarted = true;
                _initialCaller = cancellationToken;
                _initialization = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                InitializeOwnedAsync().Forget(error => _initialized.TrySetResult(error));
            }
            return ObserveAsync(_initialized, first ? CancellationToken.None : cancellationToken);
        }

        public UniTask ShutdownAsync(CancellationToken cancellationToken)
        {
            if (!_closing)
            {
                _closing = true;
                try { _initialization?.Cancel(); }
                catch (Exception error) { _failures.Add(error); }
                ShutdownOwnedAsync().Forget(error => _stopped.TrySetResult(error));
            }
            return ObserveAsync(_stopped, cancellationToken);
        }

        private object Resolve(int index)
        {
            EnsureOpen();
            RegistrationGraph.Node node = _graph.Nodes[index];
            if (node.Descriptor.Lifetime == ServiceLifetime.Root && _root != this) return _root.Resolve(index);
            if (node.Descriptor.Lifetime == ServiceLifetime.Scoped && _root == this)
                throw new InvalidOperationException($"Scoped {node.Descriptor.Contract} requires a scope.");
            if (_instances[index] != null) return _instances[index];
            if (_constructing[index]) throw new InvalidOperationException($"Reentrant construction of {node.Descriptor.Contract}.");
            if (_constructionDepth++ == 0) _constructionEnded = new UniTaskCompletionSource();
            _constructing[index] = true;
            try
            {
                foreach (int dependency in node.Dependencies) Resolve(dependency);
                EnsureOpen();
                var provider = new FactoryProvider(this, node);
                object instance;
                try { instance = node.Descriptor.Factory(provider); }
                finally { provider.Close(); }
                if (instance == null || !node.Descriptor.Contract.IsInstanceOfType(instance))
                    throw new InvalidOperationException($"Factory returned an invalid {node.Descriptor.Contract}.");
                if (!_owners.Add(instance))
                    throw new InvalidOperationException($"Factory reused an owned instance for {node.Descriptor.Contract}.");
                _instances[index] = instance;
                return instance;
            }
            finally
            {
                _constructing[index] = false;
                if (--_constructionDepth == 0) _constructionEnded.TrySetResult();
            }
        }

        private async UniTask InitializeOwnedAsync()
        {
            Exception failure = null;
            CancellationToken token = _initialization.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                if (_root != this)
                    await _root.InitializeAsync(CancellationToken.None).AttachExternalCancellation(token);
                foreach (int index in _graph.Order)
                {
                    if (!Owns(index)) continue;
                    token.ThrowIfCancellationRequested();
                    object instance = Resolve(index);
                    if (instance is IAsyncInitializable initializable)
                        await initializable.InitializeAsync(token);
                    token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception error) { failure = error; }
            _initializationFailure = failure;
            _initializationEnded.TrySetResult();
            if (failure != null || _closing)
            {
                try { await ShutdownAsync(CancellationToken.None); }
                catch (Exception error) { failure = error; }
                _initialized.TrySetResult(failure ?? new OperationCanceledException(token));
            }
            else _initialized.TrySetResult(null);
        }

        private async UniTask ShutdownOwnedAsync()
        {
            var scopes = _scopes.ToArray();
            var stops = new UniTask[scopes.Length];
            for (int i = 0; i < scopes.Length; i++) stops[i] = scopes[i].ShutdownAsync(CancellationToken.None);
            if (_constructionDepth != 0) await _constructionEnded.Task;
            if (_initializationStarted) await _initializationEnded.Task;
            if (_initializationFailure != null && !IsShutdownCancellation(_initializationFailure))
                _failures.Add(_initializationFailure);
            await ShutdownDrain.ObserveAllAsync(stops, _failures);
            for (int i = _graph.Order.Length - 1; i >= 0; i--)
            {
                int index = _graph.Order[i];
                object instance = _instances[index];
                if (instance == null) continue;
                if (instance is IAsyncStoppable stoppable)
                {
                    try { await stoppable.StopAsync(CancellationToken.None); }
                    catch (Exception error) { _failures.Add(error); }
                }
                try
                {
                    if (instance is IAsyncDisposableService asynchronous) await asynchronous.DisposeAsync();
                    else if (instance is IDisposable synchronous) synchronous.Dispose();
                }
                catch (Exception error) { _failures.Add(error); }
                _instances[index] = null;
                _owners.Remove(instance);
            }
            _scopes.Clear();
            if (_root != this) _root._scopes.Remove(this);
            _initialization?.Dispose();
            _initialization = null;
            _graph = null;
            _root = null;
            _owners = null;
            Exception failure = _failures.Count == 0 ? null : new AggregateException(_failures);
            _failures.Clear();
            _stopped.TrySetResult(failure);
        }

        private bool IsShutdownCancellation(Exception error)
        {
            return error is OperationCanceledException cancelled && _initialization != null &&
                cancelled.CancellationToken == _initialization.Token && _initialization.IsCancellationRequested &&
                !_initialCaller.IsCancellationRequested;
        }

        private bool Owns(int index)
        {
            return (_graph.Nodes[index].Descriptor.Lifetime == ServiceLifetime.Root) == (_root == this);
        }

        private void EnsureOpen()
        {
            if (_closing || _root._closing) throw new ObjectDisposedException(nameof(ServiceContainer));
        }

        private static async UniTask ObserveAsync(UniTaskCompletionSource<Exception> completion, CancellationToken token)
        {
            Exception failure = await completion.Task.AttachExternalCancellation(token);
            if (failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private sealed class InstanceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object first, object second) => ReferenceEquals(first, second);
            public int GetHashCode(object value)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }

        private sealed class FactoryProvider : IServiceProvider
        {
            private ServiceContainer _container;
            private RegistrationGraph.Node _node;

            internal FactoryProvider(ServiceContainer container, RegistrationGraph.Node node)
            {
                _container = container;
                _node = node;
            }

            public object GetService(Type serviceType)
            {
                if (_container == null) throw new ObjectDisposedException(nameof(FactoryProvider));
                foreach (int dependency in _node.Dependencies)
                {
                    if (_container._graph.Nodes[dependency].Descriptor.Contract == serviceType)
                        return _container.Resolve(dependency);
                }
                throw new InvalidOperationException(
                    $"Undeclared dependency {serviceType} of {_node.Descriptor.Contract}.");
            }

            internal void Close()
            {
                _container = null;
                _node = null;
            }
        }
    }
}
