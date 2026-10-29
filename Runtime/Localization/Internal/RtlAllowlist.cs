using System;

namespace Moirai.Atropos.L10n.Internal
{
    internal static class RtlAllowlist
    {
        internal static bool IsRtl(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return false;
            return string.Equals(languageCode, "ar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(languageCode, "he", StringComparison.OrdinalIgnoreCase);
        }
    }
}
