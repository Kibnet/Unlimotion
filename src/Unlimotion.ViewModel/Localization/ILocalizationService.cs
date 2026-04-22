using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unlimotion.ViewModel.Localization;

public interface ILocalizationService
{
    event EventHandler? CultureChanged;

    CultureInfo CurrentCulture { get; }

    string LanguageMode { get; }

    IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    void SetLanguage(string? languageMode);

    string Get(string key);

    string Format(string key, params object?[] args);

    IReadOnlyCollection<string> GetResourceKeys(CultureInfo culture);
}
