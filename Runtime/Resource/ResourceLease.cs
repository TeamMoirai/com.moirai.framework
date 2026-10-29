using System;
using Moirai.Atropos.Assets.Internal;

namespace Moirai.Atropos.Assets
{
    public readonly struct ResourceLease<T> : IDisposable where T : UnityEngine.Object
    {
        private readonly LeaseSet<T> _owner;
        private readonly long _id;

        internal ResourceLease(LeaseSet<T> owner, long id)
        {
            _owner = owner;
            _id = id;
        }

        public bool IsValid => _owner != null && _owner.IsValid(_id);
        public T Asset => _owner == null
            ? throw new ObjectDisposedException(nameof(ResourceLease<T>))
            : _owner.GetAsset(_id);

        public void Dispose() => _owner?.Release(_id);
    }
}
