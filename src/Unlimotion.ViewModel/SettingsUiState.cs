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
    Error = 4
}
