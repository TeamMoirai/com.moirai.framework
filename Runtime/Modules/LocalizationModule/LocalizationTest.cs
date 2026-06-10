using Moirai.Atropos;
using Moirai.Atropos.Localization;
using UnityEngine;

public class LocalizationTest : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Log.Warning("Test Localization(l10n) => " + LocalizationHelper.ResolveLocalizedStrings("Name:[l10n:test_name] | NoSupport:[l10n:test_name_s] | Desc:[l10n:test_desc]"));
        Log.Warning("Test Localization(i18n) => " + LocalizationHelper.ResolveLocalizedStrings("Name:[i18n:test_name] | NoSupport:[i18n:test_name_s] | Desc:[i18n:test_desc]"));
        Log.Warning("Test Localization(g11n) => " + LocalizationHelper.ResolveLocalizedStrings("Name:[g11n:test_name] | NoSupport:[g11n:test_name_s] | Desc:[g11n:test_desc]"));
    }
}