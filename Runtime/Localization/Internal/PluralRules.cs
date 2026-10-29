using System;

namespace Moirai.Atropos.L10n.Internal
{
    internal static class PluralRules
    {
        internal static string Select(string languageCode, long count, string one, string other)
        {
            if (string.IsNullOrEmpty(languageCode)) throw new ArgumentException(nameof(languageCode));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            bool singular = count == 1 &&
                !languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
                !languageCode.StartsWith("ja", StringComparison.OrdinalIgnoreCase) &&
                !languageCode.StartsWith("ko", StringComparison.OrdinalIgnoreCase);
            return singular ? one : other;
        }
    }
}
