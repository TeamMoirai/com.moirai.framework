using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.L10n
{
    public interface ILocalizationService
    {
        event Action<string> LanguageChanged;
        string CurrentLanguage { get; }
        IReadOnlyList<string> SupportedLanguages { get; }
        UniTask SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default);
        string GetText(string key);
    }
}
