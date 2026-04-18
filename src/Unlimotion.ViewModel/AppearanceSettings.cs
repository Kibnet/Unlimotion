using System;

namespace Unlimotion.ViewModel;

public static class AppearanceSettings
{
    public const string SectionName = "Appearance";
    public const string ThemeKey = "Theme";
    public const string FontSizeKey = "FontSize";
    public const string SystemTheme = "System";
    public const string DarkTheme = "Dark";
    public const string LightTheme = "Light";
    public const double DefaultFontSize = 12;
    public const double MinFontSize = 10;
    public const double MaxFontSize = 24;
    private const double FloatingWatermarkFontSizeOffset = -2;
    private const double TabFontSizeOffset = 2;
    private const double SearchClearIconFontSizeOffset = 2;
    private const double MinTabFontSize = 14;
    private const double MaxTabFontSize = 18;
    private const double MaxSearchClearIconFontSize = 22;
    private const double TabMinHeightPadding = 14;
    private const double SearchControlHeightPadding = 24;
    private const double SearchClearButtonPadding = 18;
    private const double FloatingControlMinHeightPadding = 30;
    private const double SearchBarMinWidthBase = 280;
    private const double SearchBarMinWidthScale = 4;

    public static double DefaultSmallFontSize => GetFloatingWatermarkFontSize(DefaultFontSize);

    public static double DefaultTabFontSize => GetTabFontSize(DefaultFontSize);

    public static double DefaultTabMinHeight => GetTabMinHeight(DefaultFontSize);

    public static double DefaultSearchControlHeight => GetSearchControlHeight(DefaultFontSize);

    public static double DefaultSearchClearButtonSize => GetSearchClearButtonSize(DefaultFontSize);

    public static double DefaultSearchClearIconFontSize => GetSearchClearIconFontSize(DefaultFontSize);

    public static double DefaultSearchBarMinWidth => GetSearchBarMinWidth(DefaultFontSize);

    public static double DefaultFloatingControlMinHeight => GetFloatingControlMinHeight(DefaultFontSize);

    public static ThemeMode ParseThemeMode(string? configuredTheme)
    {
        return configuredTheme?.Trim().ToLowerInvariant() switch
        {
            "dark" => ThemeMode.Dark,
            "light" => ThemeMode.Light,
            "system" => ThemeMode.System,
            _ => ThemeMode.System
        };
    }

    public static bool? ParseIsDarkTheme(string? configuredTheme)
    {
        return ParseThemeMode(configuredTheme) switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => null
        };
    }

    public static string ToStoredTheme(ThemeMode themeMode)
    {
        return themeMode switch
        {
            ThemeMode.Dark => DarkTheme,
            ThemeMode.Light => LightTheme,
            _ => SystemTheme
        };
    }

    public static string ToStoredTheme(bool isDarkTheme)
    {
        return ToStoredTheme(isDarkTheme ? ThemeMode.Dark : ThemeMode.Light);
    }

    public static double NormalizeFontSize(double? configuredFontSize)
    {
        var fontSize = configuredFontSize.GetValueOrDefault(DefaultFontSize);
        if (double.IsNaN(fontSize) || double.IsInfinity(fontSize))
        {
            return DefaultFontSize;
        }

        return Math.Clamp(fontSize, MinFontSize, MaxFontSize);
    }

    public static double GetFloatingWatermarkFontSize(double baseFontSize)
    {
        return Math.Max(8, NormalizeFontSize(baseFontSize) + FloatingWatermarkFontSizeOffset);
    }

    public static double GetTabFontSize(double baseFontSize)
    {
        return Math.Clamp(
            NormalizeFontSize(baseFontSize) + TabFontSizeOffset,
            MinTabFontSize,
            MaxTabFontSize);
    }

    public static double GetSearchClearIconFontSize(double baseFontSize)
    {
        return Math.Clamp(
            NormalizeFontSize(baseFontSize) + SearchClearIconFontSizeOffset,
            MinTabFontSize,
            MaxSearchClearIconFontSize);
    }

    public static double GetTabMinHeight(double baseFontSize)
    {
        return Math.Max(28, GetTabFontSize(baseFontSize) + TabMinHeightPadding);
    }

    public static double GetSearchControlHeight(double baseFontSize)
    {
        return Math.Max(36, NormalizeFontSize(baseFontSize) + SearchControlHeightPadding);
    }

    public static double GetSearchClearButtonSize(double baseFontSize)
    {
        return Math.Max(30, NormalizeFontSize(baseFontSize) + SearchClearButtonPadding);
    }

    public static double GetSearchBarMinWidth(double baseFontSize)
    {
        return Math.Clamp(
            SearchBarMinWidthBase + (NormalizeFontSize(baseFontSize) * SearchBarMinWidthScale),
            320,
            380);
    }

    public static double GetFloatingControlMinHeight(double baseFontSize)
    {
        return Math.Max(42, NormalizeFontSize(baseFontSize) + FloatingControlMinHeightPadding);
    }
}
