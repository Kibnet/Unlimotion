using System.Text.Json;

namespace Unlimotion.Cli;

public static class TaskDirectoryResolver
{
    public static string Resolve(string? explicitTasksPath, string settingsPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitTasksPath))
        {
            return explicitTasksPath;
        }

        if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
        {
            throw SettingsError("settingsNotFound", "Unlimotion desktop settings were not found. Specify --tasks <path> explicitly.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(document.RootElement, "TaskStorage", out var taskStorage) ||
                taskStorage.ValueKind != JsonValueKind.Object)
            {
                throw SettingsError("settingsPathMissing", "Unlimotion desktop settings do not define TaskStorage.Path. Specify --tasks <path> explicitly.");
            }

            if (TryGetProperty(taskStorage, "IsServerMode", out var serverMode))
            {
                var isServerMode = serverMode.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(serverMode.GetString(), out var parsed) => parsed,
                    _ => throw SettingsError("settingsInvalid", "Unlimotion desktop settings contain an invalid TaskStorage.IsServerMode value. Specify --tasks <path> explicitly.")
                };

                if (isServerMode)
                {
                    throw SettingsError("settingsUnsupported", "The active Unlimotion task space is a server source. Specify --tasks <path> for a local task directory.");
                }
            }

            if (!TryGetProperty(taskStorage, "Path", out var path) ||
                path.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(path.GetString()))
            {
                throw SettingsError("settingsPathMissing", "Unlimotion desktop settings do not define TaskStorage.Path. Specify --tasks <path> explicitly.");
            }

            return path.GetString()!;
        }
        catch (CliException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw SettingsError("settingsInvalid", "Unlimotion desktop settings are not valid JSON. Specify --tasks <path> explicitly.");
        }
        catch (IOException)
        {
            throw SettingsError("settingsInvalid", "Unlimotion desktop settings cannot be read. Specify --tasks <path> explicitly.");
        }
        catch (UnauthorizedAccessException)
        {
            throw SettingsError("settingsInvalid", "Unlimotion desktop settings cannot be read. Specify --tasks <path> explicitly.");
        }
    }

    public static string Resolve(string? explicitTasksPath) => Resolve(
        explicitTasksPath,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Unlimotion", "Settings.json"));

    private static CliException SettingsError(string kind, string message) => new(message, exitCode: 1, kind);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
