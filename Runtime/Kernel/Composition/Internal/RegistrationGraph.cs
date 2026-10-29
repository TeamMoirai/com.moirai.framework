using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Kernel.Composition.Internal
{
    internal sealed class RegistrationGraph : IServiceRegistrar
    {
        internal sealed class Node
        {
            internal readonly ServiceDescriptor Descriptor;
            internal readonly string Module;
            internal int[] Dependencies;

            internal Node(ServiceDescriptor descriptor, string module)
            {
                Descriptor = descriptor;
                Module = module;
            }
        }

        private readonly Dictionary<Type, int> _indices = new Dictionary<Type, int>();
        private readonly List<Node> _nodes = new List<Node>();
        private string _registeringModule;

        internal Node[] Nodes { get; }
        internal int[] Order { get; }

        internal RegistrationGraph(IServiceModule[] modules)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            foreach (IServiceModule module in modules)
            {
                if (module == null) throw new ArgumentException("A module cannot be null.", nameof(modules));
                _registeringModule = module.GetType().FullName;
                try { module.Register(this); }
                finally { _registeringModule = null; }
            }
            Nodes = _nodes.ToArray();
            foreach (Node node in Nodes)
            {
                Type[] dependencies = node.Descriptor.Dependencies;
                node.Dependencies = new int[dependencies.Length];
                for (int i = 0; i < dependencies.Length; i++) node.Dependencies[i] = IndexOf(dependencies[i]);
            }
            var colors = new byte[Nodes.Length];
            var path = new List<int>();
            var order = new List<int>(Nodes.Length);
            for (int i = 0; i < Nodes.Length; i++) Visit(i, colors, path, order);
            Order = order.ToArray();
            foreach (int index in Order)
            {
                Node node = Nodes[index];
                if (node.Descriptor.Lifetime != ServiceLifetime.Root) continue;
                foreach (int dependency in node.Dependencies)
                {
                    if (Nodes[dependency].Descriptor.Lifetime == ServiceLifetime.Scoped)
                    {
                        throw new InvalidOperationException(
                            $"{node.Descriptor.Contract} captures scoped {Nodes[dependency].Descriptor.Contract}.");
                    }
                }
            }
        }

        public void Add(in ServiceDescriptor descriptor)
        {
            if (_registeringModule == null) throw new InvalidOperationException("Registration is closed.");
            ValidateContract(descriptor.Contract);
            if (descriptor.Factory == null) throw new ArgumentException("A factory is required.", nameof(descriptor));
            if (descriptor.Dependencies == null) throw new ArgumentException("Dependencies are required.", nameof(descriptor));
            if (descriptor.Lifetime != ServiceLifetime.Root && descriptor.Lifetime != ServiceLifetime.Scoped)
                throw new ArgumentOutOfRangeException(nameof(descriptor));
            if (_indices.TryGetValue(descriptor.Contract, out int existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate {descriptor.Contract} in {_nodes[existing].Module} and {_registeringModule}.");
            }
            var dependencies = (Type[])descriptor.Dependencies.Clone();
            foreach (Type dependency in dependencies) ValidateContract(dependency);
            var frozen = new ServiceDescriptor(descriptor.Contract, dependencies,
                descriptor.Lifetime, descriptor.Factory);
            _indices.Add(descriptor.Contract, _nodes.Count);
            _nodes.Add(new Node(frozen, _registeringModule));
        }

        internal int IndexOf(Type contract)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            if (!_indices.TryGetValue(contract, out int index))
                throw new InvalidOperationException($"Missing registration for {contract}.");
            return index;
        }

        private void Visit(int index, byte[] colors, List<int> path, List<int> order)
        {
            if (colors[index] == 2) return;
            if (colors[index] == 1)
            {
                int start = path.IndexOf(index);
                var cycle = new string[path.Count - start + 1];
                for (int i = start; i < path.Count; i++)
                    cycle[i - start] = Nodes[path[i]].Descriptor.Contract.Name;
                cycle[cycle.Length - 1] = Nodes[index].Descriptor.Contract.Name;
                throw new InvalidOperationException("Dependency cycle: " + string.Join(" -> ", cycle));
            }
            colors[index] = 1;
            path.Add(index);
            foreach (int dependency in Nodes[index].Dependencies) Visit(dependency, colors, path, order);
            path.RemoveAt(path.Count - 1);
            colors[index] = 2;
            order.Add(index);
        }

        private static void ValidateContract(Type contract)
        {
            if (contract == null) throw new ArgumentException("A contract cannot be null.");
            if (contract.IsValueType || contract.IsPointer || contract.IsByRef || contract.ContainsGenericParameters)
                throw new ArgumentException($"A closed reference contract is required: {contract}.");
            if (contract == typeof(IServiceProvider) || contract == typeof(ServiceContainer))
                throw new ArgumentException($"{contract} cannot be injected into services.");
        }
    }
}
