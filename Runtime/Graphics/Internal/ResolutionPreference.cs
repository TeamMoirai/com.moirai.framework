using System;
using System.Globalization;

namespace Moirai.Atropos.Rendering.Internal
{
    internal static class ResolutionPreference
    {
        public static string Encode(ResolutionInfo value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}x{1}@{2}/{3}",
                value.Width, value.Height, value.RateNumerator, value.RateDenominator);
        }

        public static ResolutionInfo Decode(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            int x = value.IndexOf('x');
            int at = x < 0 ? -1 : value.IndexOf('@', x + 1);
            int slash = at < 0 ? -1 : value.IndexOf('/', at + 1);
            if (x <= 0 || at <= x + 1 || slash <= at + 1 || value.IndexOf('/', slash + 1) >= 0)
                throw new FormatException("Resolution text is malformed: " + value);

            if (!int.TryParse(value.Substring(0, x), NumberStyles.None, CultureInfo.InvariantCulture,
                    out int width) ||
                !int.TryParse(value.Substring(x + 1, at - x - 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int height) ||
                !int.TryParse(value.Substring(at + 1, slash - at - 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int numerator) ||
                !int.TryParse(value.Substring(slash + 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int denominator) ||
                width <= 0 || height <= 0 || numerator == 0 || denominator == 0)
                throw new FormatException("Resolution text is out of range: " + value);

            return new ResolutionInfo(width, height, numerator, denominator);
        }
    }
}
