using System.Globalization;

namespace Unlimotion.ViewModel.Localization;

public static class Localization
{
    public static ILocalizationService Current => LocalizationService.Current;

    public static string Get(string key) => Current.Get(key);

    public static string Format(string key, params object?[] args) => Current.Format(key, args);

    public static void SetLanguage(string? languageMode) => Current.SetLanguage(languageMode);

    public static string NormalizeLanguageMode(string? languageMode)
    {
        return languageMode?.Trim().ToLowerInvariant() switch
        {
            LocalizationService.EnglishLanguage => LocalizationService.EnglishLanguage,
            LocalizationService.RussianLanguage => LocalizationService.RussianLanguage,
            "system" or "" or null => LocalizationService.SystemLanguage,
            _ => LocalizationService.SystemLanguage
        };
    }

    public static bool IsSupportedSpecificOrNeutral(CultureInfo culture)
    {
        return culture.TwoLetterISOLanguageName is LocalizationService.EnglishLanguage or LocalizationService.RussianLanguage;
    }
}
