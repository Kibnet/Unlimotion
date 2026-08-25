namespace Unlimotion.ViewModel;

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum BackupAuthMode
{
    Token = 0,
    Ssh = 1
}

public enum SettingsConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Error = 3
}

public enum BackupStatusState
{
    NotConfigured = 0,
    Connecting = 1,
    Connected = 2,
    Syncing = 3,
    Error = 4,
    ConflictResolution = 5
}

/// <summary>
/// The UI-safe result of validating a daily note filename format.
/// The Notes layer owns the actual domain validation; this DTO lets Settings
/// present its result without taking a direct dependency on the storage implementation.
/// </summary>
public sealed record NoteDailyFileNameFormatValidation(
    bool IsValid,
    string? PreviewPath,
    string? ErrorMessage);

/// <summary>
/// The current portable daily-note filename setting for one vault. SessionGeneration changes
/// whenever Feed replaces the active vault session, including a same-root rebind.
/// </summary>
public sealed record NoteDailyFileNameFormatState(
    string FileNameFormat,
    string? RootPath,
    string? Revision,
    string? StatusMessage = null,
    bool IsExternalChange = false,
    long SessionGeneration = 0,
    bool RequiresReload = false);

/// <summary>
/// A non-throwing result returned when the Feed applies a new portable setting.
/// </summary>
public sealed record NoteDailyFileNameFormatApplyResult(
    bool Succeeded,
    NoteDailyFileNameFormatState? AppliedState = null,
    string? ErrorMessage = null,
    bool IsCancelled = false);
