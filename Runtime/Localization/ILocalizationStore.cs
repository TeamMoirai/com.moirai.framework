using System.Collections.Generic;

namespace Moirai.Atropos.L10n
{
    public interface ILocalizationStore
    {
        IReadOnlyList<string> SupportedLanguages { get; }
        bool TryGet(string language, string key, out string text);
    }
}
