namespace Unlimotion.ViewModel;

public static class AppearanceSettings
{
    public const string SectionName = "Appearance";
    public const string ThemeKey = "Theme";
    public const string DarkTheme = "Dark";
    public const string LightTheme = "Light";

    public static bool? ParseIsDarkTheme(string? configuredTheme)
    {
        return configuredTheme?.Trim().ToLowerInvariant() switch
        {
            "dark" => true,
            "light" => false,
            _ => null
        };
    }

    public static string ToStoredTheme(bool isDarkTheme)
    {
        return isDarkTheme ? DarkTheme : LightTheme;
    }
}
