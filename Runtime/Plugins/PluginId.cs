using System;

namespace Moirai.Atropos.Plugins
{
    public readonly struct PluginId : IEquatable<PluginId>
    {
        public PluginId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(nameof(value));
            Value = value;
        }

        public string Value { get; }
        public bool Equals(PluginId other) => StringComparer.Ordinal.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is PluginId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }
}
