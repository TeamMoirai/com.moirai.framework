using System;

namespace Moirai.Atropos.Plugins
{
    public readonly struct PluginVersion : IComparable<PluginVersion>
    {
        public PluginVersion(int major, int minor, int patch)
        {
            if (major < 0 || minor < 0 || patch < 0) throw new ArgumentOutOfRangeException(nameof(major));
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public int CompareTo(PluginVersion other)
        {
            int result = Major.CompareTo(other.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(other.Minor);
            return result != 0 ? result : Patch.CompareTo(other.Patch);
        }

        public override string ToString() => Major + "." + Minor + "." + Patch;
    }
}
