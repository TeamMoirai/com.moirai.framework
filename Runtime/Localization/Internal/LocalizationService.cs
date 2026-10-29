using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Settings;

namespace Moirai.Atropos.L10n.Internal
{
    internal sealed class LocalizationService : ILocalizationService
    {
        private readonly ILocalizationStore _store;
        private readonly IKeyValueStore _settings;
        private int _switchVersion;

        internal LocalizationService(ILocalizationStore store, IKeyValueStore settings,
            string initialLanguage)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            string stored = _settings.GetString(LocalizationSettingKeys.Language, initialLanguage);
            if (IsSupported(stored)) CurrentLanguage = stored;
            else if (IsSupported(initialLanguage)) CurrentLanguage = initialLanguage;
            else
                throw new ArgumentException("Unsupported initial language: " + initialLanguage,
                    nameof(initialLanguage));
        }

        public event Action<string> LanguageChanged;

        public string CurrentLanguage { get; private set; }
        public IReadOnlyList<string> SupportedLanguages => _store.SupportedLanguages;

        public async UniTask SetLanguageAsync(string languageCode,
            CancellationToken cancellationToken = default)
        {
            if (!IsSupported(languageCode))
                throw new ArgumentException("Unsupported language: " + languageCode, nameof(languageCode));
            int version = ++_switchVersion;
            await UniTask.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (version != _switchVersion) return;
            string previous = CurrentLanguage;
            CurrentLanguage = languageCode;
            if (string.Equals(previous, languageCode, StringComparison.Ordinal)) return;
            _settings.SetString(LocalizationSettingKeys.Language, languageCode);
            LanguageChanged?.Invoke(languageCode);
        }

        public string GetText(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return _store.TryGet(CurrentLanguage, key, out string text) ? text : key;
        }

        private bool IsSupported(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return false;
            IReadOnlyList<string> languages = _store.SupportedLanguages;
            for (int i = 0; i < languages.Count; i++)
                if (string.Equals(languages[i], languageCode, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
