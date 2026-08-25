using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Unlimotion.Notes.Daily;

/// <summary>
/// Defines the one active, safe filename layout for daily Markdown notes in a vault.
/// </summary>
public sealed class DailyNoteNaming
{
    public const string DefaultFileNameFormat = "yyyy-MM-dd";
    public const string DailyDirectoryName = "Ежедневные";
    public const string DailyFileExtension = ".md";

    private static readonly DateOnly[] SentinelDates =
    [
        new DateOnly(2001, 2, 3),
        new DateOnly(2024, 2, 29),
        new DateOnly(2098, 11, 30)
    ];

    private DailyNoteNaming(string fileNameFormat)
    {
        FileNameFormat = fileNameFormat;
    }

    public static DailyNoteNaming Default { get; } = Create(DefaultFileNameFormat);

    /// <summary>
    /// Gets the validated .NET date format used only for the filename stem.
    /// </summary>
    public string FileNameFormat { get; }

    public static DailyNoteNaming Create(string fileNameFormat)
    {
        if (!TryCreate(fileNameFormat, out var naming, out var validationError))
        {
            throw new ArgumentException(validationError, nameof(fileNameFormat));
        }

        return naming;
    }

    public static bool TryCreate(
        string? fileNameFormat,
        [NotNullWhen(true)] out DailyNoteNaming? naming,
        [NotNullWhen(false)] out string? validationError)
    {
        naming = null;
        validationError = null;

        if (!TryValidateGrammar(fileNameFormat, out validationError))
        {
            validationError ??= "The daily note filename format is invalid.";
            return false;
        }

        foreach (var sentinel in SentinelDates)
        {
            string stem;
            try
            {
                stem = sentinel.ToString(fileNameFormat, CultureInfo.InvariantCulture);
            }
            catch (FormatException exception)
            {
                validationError = $"The daily note format is invalid: {exception.Message}";
                return false;
            }

            if (!IsSafeFileNameStem(stem))
            {
                validationError = "The daily note format produces an unsafe filename.";
                return false;
            }

            if (!DateOnly.TryParseExact(
                    stem,
                    fileNameFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed)
                || parsed != sentinel)
            {
                validationError = "The daily note format must round-trip a calendar date exactly.";
                return false;
            }
        }

        naming = new DailyNoteNaming(fileNameFormat!);
        return true;
    }

    public string FormatStem(DateOnly date) => date.ToString(FileNameFormat, CultureInfo.InvariantCulture);

    public string GetRelativePath(DateOnly date) =>
        $"{DailyDirectoryName}/{FormatStem(date)}{DailyFileExtension}";

    public bool TryParseRelativePath(string? relativePath, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');
        var prefix = DailyDirectoryName + "/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = normalized[prefix.Length..];
        if (fileName.Length == 0
            || fileName.Contains('/', StringComparison.Ordinal)
            || !fileName.EndsWith(DailyFileExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var stem = fileName[..^DailyFileExtension.Length];
        if (!DateOnly.TryParseExact(
                stem,
                FileNameFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
            || !string.Equals(FormatStem(parsed), stem, StringComparison.Ordinal))
        {
            return false;
        }

        date = parsed;
        return true;
    }

    public bool IsDailyRelativePath(string? relativePath) =>
        TryParseRelativePath(relativePath, out _);

    private static bool TryValidateGrammar(string? fileNameFormat, out string? validationError)
    {
        validationError = null;
        if (string.IsNullOrEmpty(fileNameFormat))
        {
            validationError = "Enter yyyy, MM and dd in the daily note filename format.";
            return false;
        }

        var components = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;
        while (position < fileNameFormat.Length)
        {
            var component = ReadComponent(fileNameFormat, ref position);
            if (component is null)
            {
                validationError = "Use yyyy, MM and dd; only -, . and _ may separate them.";
                return false;
            }

            if (!components.Add(component))
            {
                validationError = "Use each of yyyy, MM and dd exactly once.";
                return false;
            }

            if (position == fileNameFormat.Length)
            {
                break;
            }

            var separator = fileNameFormat[position];
            if (separator is '-' or '.' or '_')
            {
                position++;
                if (position == fileNameFormat.Length)
                {
                    validationError = "Use each of yyyy, MM and dd exactly once.";
                    return false;
                }

                continue;
            }

            // Adjacent numeric components such as yyyyMMdd are also part of the
            // supported grammar. The next iteration validates the next token.
        }

        if (components.Count != 3
            || !components.Contains("yyyy")
            || !components.Contains("MM")
            || !components.Contains("dd"))
        {
            validationError = "Use each of yyyy, MM and dd exactly once.";
            return false;
        }

        return true;
    }

    private static string? ReadComponent(string format, ref int position)
    {
        foreach (var component in new[] { "yyyy", "MM", "dd" })
        {
            if (format.AsSpan(position).StartsWith(component, StringComparison.Ordinal))
            {
                position += component.Length;
                return component;
            }
        }

        return null;
    }

    private static bool IsSafeFileNameStem(string stem)
    {
        return !string.IsNullOrEmpty(stem)
            && !stem.EndsWith('.')
            && !stem.EndsWith(' ')
            && stem.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && stem.All(static character => !char.IsControl(character)
                                         && character is not '#' and not '[' and not ']'
                                         && character is not '/' and not '\\');
    }
}
