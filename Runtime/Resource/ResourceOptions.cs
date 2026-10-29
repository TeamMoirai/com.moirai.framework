using System;

namespace Moirai.Atropos.Assets
{
    public sealed class ResourceOptions
    {
        public int LeaseCapacity { get; set; } = 64;
        public double KeepAliveSeconds { get; set; } = 5d;
        public int MaxEvictionsPerUpdate { get; set; } = 32;
        public string DefaultPackage { get; set; } = string.Empty;

        internal void Validate()
        {
            if (LeaseCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(LeaseCapacity));
            if (KeepAliveSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(KeepAliveSeconds));
            if (MaxEvictionsPerUpdate <= 0) throw new ArgumentOutOfRangeException(nameof(MaxEvictionsPerUpdate));
            if (DefaultPackage == null) throw new ArgumentNullException(nameof(DefaultPackage));
        }
    }
}
