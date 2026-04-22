using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;

namespace Unlimotion.ViewModel.Localization;

public sealed class LocalizationService : ILocalizationService
{
    public const string SystemLanguage = "System";
    public const string EnglishLanguage = "en";
    public const string RussianLanguage = "ru";

    private static readonly ResourceManager ResourceManager =
        new("Unlimotion.ViewModel.Resources.Strings", typeof(LocalizationService).Assembly);

    private readonly ILocalizationSystemCultureProvider _systemCultureProvider;
    private CultureInfo _currentCulture = CultureInfo.GetCultureInfo(EnglishLanguage);
    private string _languageMode = SystemLanguage;
    private IReadOnlyList<LanguageOption> _supportedLanguages = Array.Empty<LanguageOption>();

    public LocalizationService(ILocalizationSystemCultureProvider? systemCultureProvider = null)
    {
        _systemCultureProvider = systemCultureProvider ?? new DefaultLocalizationSystemCultureProvider();
        SetLanguage(SystemLanguage);
    }

    public event EventHandler? CultureChanged;

    public CultureInfo CurrentCulture => _currentCulture;

    public string LanguageMode => _languageMode;

    public IReadOnlyList<LanguageOption> SupportedLanguages => _supportedLanguages;

    public static ILocalizationService Current { get; set; } = new LocalizationService();

    public void SetLanguage(string? languageMode)
    {
        var normalizedMode = NormalizeLanguageMode(languageMode);
        var effectiveCulture = ResolveEffectiveCulture(normalizedMode);

        _languageMode = normalizedMode;
        _currentCulture = effectiveCulture;

        CultureInfo.DefaultThreadCurrentUICulture = effectiveCulture;
        CultureInfo.DefaultThreadCurrentCulture = effectiveCulture;
        Thread.CurrentThread.CurrentUICulture = effectiveCulture;
        Thread.CurrentThread.CurrentCulture = effectiveCulture;

        RefreshSupportedLanguages();
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return ResourceManager.GetString(key, _currentCulture)
               ?? ResourceManager.GetString(key, CultureInfo.GetCultureInfo(EnglishLanguage))
               ?? key;
    }

    public string Format(string key, params object?[] args)
    {
        return string.Format(_currentCulture, Get(key), args);
    }

    public IReadOnlyCollection<string> GetResourceKeys(CultureInfo culture)
    {
        var resourceSet = ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        if (resourceSet == null)
        {
            return Array.Empty<string>();
        }

        return resourceSet
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => entry.Key.ToString() ?? string.Empty)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeLanguageMode(string? languageMode)
    {
        return languageMode?.Trim().ToLowerInvariant() switch
        {
            EnglishLanguage => EnglishLanguage,
            RussianLanguage => RussianLanguage,
            "system" or "" or null => SystemLanguage,
            _ => SystemLanguage
        };
    }

    private CultureInfo ResolveEffectiveCulture(string languageMode)
    {
        return languageMode switch
        {
            EnglishLanguage => CultureInfo.GetCultureInfo(EnglishLanguage),
            RussianLanguage => CultureInfo.GetCultureInfo(RussianLanguage),
            _ => ResolveSystemCulture()
        };
    }

    private CultureInfo ResolveSystemCulture()
    {
        var cultureName = _systemCultureProvider.SystemUICulture.TwoLetterISOLanguageName;
        return cultureName switch
        {
            RussianLanguage => CultureInfo.GetCultureInfo(RussianLanguage),
            EnglishLanguage => CultureInfo.GetCultureInfo(EnglishLanguage),
            _ => CultureInfo.GetCultureInfo(EnglishLanguage)
        };
    }

    private void RefreshSupportedLanguages()
    {
        _supportedLanguages =
        [
            new LanguageOption(SystemLanguage, Get("LanguageSystem")),
            new LanguageOption(EnglishLanguage, Get("LanguageEnglish")),
            new LanguageOption(RussianLanguage, Get("LanguageRussian"))
        ];
    }
}
