using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unlimotion.Services;

public static class GitSafeDirectoryConfig
{
    public static void EnsureSafeDirectory(string configPath, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        var safeDirectory = NormalizeDirectoryPath(directoryPath);
        if (string.IsNullOrWhiteSpace(safeDirectory))
        {
            return;
        }

        var configDirectory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            Directory.CreateDirectory(configDirectory);
        }

        var lines = File.Exists(configPath)
            ? File.ReadAllLines(configPath).ToList()
            : new List<string>();

        if (ContainsSafeDirectory(lines, safeDirectory))
        {
            return;
        }

        var safeSectionIndex = lines.FindIndex(IsSafeSectionHeader);
        if (safeSectionIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            lines.Add("[safe]");
            lines.Add(FormatSafeDirectory(safeDirectory));
        }
        else
        {
            var insertIndex = safeSectionIndex + 1;
            while (insertIndex < lines.Count && !IsSectionHeader(lines[insertIndex]))
            {
                insertIndex++;
            }

            lines.Insert(insertIndex, FormatSafeDirectory(safeDirectory));
        }

        File.WriteAllLines(configPath, lines);
    }

    public static string NormalizeDirectoryPath(string directoryPath)
    {
        return directoryPath
            .Replace('\\', '/')
            .TrimEnd('/')
            .Trim();
    }

    private static bool ContainsSafeDirectory(IEnumerable<string> lines, string safeDirectory)
    {
        return lines
            .Select(TryGetSafeDirectoryValue)
            .Any(value => string.Equals(value, safeDirectory, StringComparison.Ordinal));
    }

    private static string? TryGetSafeDirectoryValue(string line)
    {
        var separatorIndex = line.IndexOf('=');
        if (separatorIndex < 0)
        {
            return null;
        }

        var key = line[..separatorIndex].Trim();
        if (!string.Equals(key, "directory", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NormalizeDirectoryPath(line[(separatorIndex + 1)..]);
    }

    private static bool IsSafeSectionHeader(string line)
    {
        return string.Equals(line.Trim(), "[safe]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSectionHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("[", StringComparison.Ordinal) &&
               trimmed.EndsWith("]", StringComparison.Ordinal);
    }

    private static string FormatSafeDirectory(string safeDirectory)
    {
        return $"\tdirectory = {safeDirectory}";
    }
}
