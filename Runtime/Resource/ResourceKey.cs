using System;

namespace Moirai.Atropos.Assets
{
    public readonly struct ResourceKey : IEquatable<ResourceKey>
    {
        public ResourceKey(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location)) throw new ArgumentException(nameof(location));
            Location = location;
            PackageName = packageName ?? string.Empty;
        }

        public string Location { get; }
        public string PackageName { get; }

        public bool Equals(ResourceKey other) =>
            string.Equals(Location, other.Location, StringComparison.Ordinal) &&
            string.Equals(PackageName, other.PackageName, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ResourceKey other && Equals(other);
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Location) * 397 ^ StringComparer.Ordinal.GetHashCode(PackageName);
        public override string ToString() =>
            PackageName.Length == 0 ? Location : PackageName + ":" + Location;
    }
}
