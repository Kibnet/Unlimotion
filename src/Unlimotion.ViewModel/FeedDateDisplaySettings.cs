using System;
using System.Globalization;

namespace Unlimotion.ViewModel;

/// <summary>Application presentation only; never used to resolve or rename daily note files.</summary>
public static class FeedDateDisplaySettings
{
    public const string SectionName = "FeedAppearance";
    public const string FormatKey = "DisplayDateFormat";
    public const string DefaultFormat = "d MMMM yyyy, dddd";
    public const int MaximumFormatLength = 128;

    public static bool IsValidFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Length > MaximumFormatLength ||
            format.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0)
            return false;

        try
        {
            // DateOnly rejects time-only specifiers as well as malformed custom formats.
            return !string.IsNullOrWhiteSpace(new DateOnly(2026, 9, 9).ToString(format, CultureInfo.InvariantCulture));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string NormalizeFormat(string? format) => IsValidFormat(format) ? format! : DefaultFormat;

    public static string Format(DateOnly date, string? format, CultureInfo culture) =>
        date.ToString(NormalizeFormat(format), culture);
}
