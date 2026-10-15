using System;
using System.Globalization;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 插件语义版本号，固定三段十进制。
    /// </summary>
    /// <remarks>
    /// 段数不等于三、带预发布后缀、含符号或空白一律判非法——不做"尽力解析后跳过比较"，那种降级会让依赖检查假通过。<br />
    /// 不可变值类型；比较按 Major→Minor→Patch 字典序；未解析的默认值与各段皆零的显式值同序，比较语义只在解析成功后使用。
    /// </remarks>
    internal readonly struct PluginVersion : IEquatable<PluginVersion>, IComparable<PluginVersion>
    {
        private const int SegmentCount = 3;

        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;

        public PluginVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        /// <summary>解析三段版本号。</summary>
        public static bool TryParse(string text, out PluginVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(text)) return false;

            string[] segments = text.Split('.');
            if (segments.Length != SegmentCount) return false;

            var numbers = new int[SegmentCount];
            for (int i = 0; i < SegmentCount; i++)
            {
                if (!int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return false;
            }

            version = new PluginVersion(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        /// <summary>按主/次/修订次序比较。</summary>
        public int CompareTo(PluginVersion other)
        {
            int byMajor = Major.CompareTo(other.Major);
            if (byMajor != 0) return byMajor;

            int byMinor = Minor.CompareTo(other.Minor);
            if (byMinor != 0) return byMinor;

            return Patch.CompareTo(other.Patch);
        }

        public bool Equals(PluginVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

        public override bool Equals(object obj) => obj is PluginVersion other && Equals(other);

        public override int GetHashCode() => (Major * 39323) + (Minor * 31) + Patch;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, Patch);

        public static bool operator ==(PluginVersion left, PluginVersion right) => left.Equals(right);

        public static bool operator !=(PluginVersion left, PluginVersion right) => !left.Equals(right);

        public static bool operator <(PluginVersion left, PluginVersion right) => left.CompareTo(right) < 0;

        public static bool operator >(PluginVersion left, PluginVersion right) => left.CompareTo(right) > 0;

        public static bool operator <=(PluginVersion left, PluginVersion right) => left.CompareTo(right) <= 0;

        public static bool operator >=(PluginVersion left, PluginVersion right) => left.CompareTo(right) >= 0;
    }
}
