using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AppAutomation.Session.Contracts;

namespace Unlimotion.AppAutomation.TestHost;

public enum SettingsFileDamage
{
    None,
    EmptyWithBackup,
    TruncatedWithBackup,
    MissingWithBackup,
    EmptyWithoutBackup,
    TruncatedWithoutBackup,
    InvalidRootWithoutBackup,
    MissingWithInvalidBackup,
    ValidWithRestrictedBackup
}

/// <summary>Owns one synthetic profile across desktop restarts; never uses installed settings.</summary>
public sealed class SettingsFileRecoveryTestFixture : IDisposable
{
    private readonly DesktopAppLaunchOptions _ownedOptions;
    private readonly string? _publishedExecutable;
    private readonly string _windowTitle = "Unlimotion Settings Recovery " + Guid.NewGuid().ToString("N");

    public SettingsFileRecoveryTestFixture(SettingsFileDamage damage, string? publishedExecutable = null)
    {
        _publishedExecutable = publishedExecutable ??
            Environment.GetEnvironmentVariable("UNLIMOTION_SETTINGS_RECOVERY_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(_publishedExecutable) && !File.Exists(_publishedExecutable))
        {
            throw new FileNotFoundException("Published executable for the recovery smoke test does not exist.", _publishedExecutable);
        }
        _ownedOptions = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            buildBeforeLaunch: false,
            mainWindowTimeout: TimeSpan.FromSeconds(30));
        ConfigPath = _ownedOptions.Arguments.Single(argument =>
            argument.StartsWith("--config=", StringComparison.Ordinal))["--config=".Length..];
        using var original = JsonDocument.Parse(File.ReadAllBytes(ConfigPath));
        TasksPath = original.RootElement.GetProperty("TaskStorage").GetProperty("Path").GetString()!;

        if (damage is SettingsFileDamage.EmptyWithBackup or SettingsFileDamage.TruncatedWithBackup or
            SettingsFileDamage.MissingWithBackup)
        {
            File.Copy(ConfigPath, BackupPath);
        }

        switch (damage)
        {
            case SettingsFileDamage.EmptyWithBackup:
            case SettingsFileDamage.EmptyWithoutBackup:
                File.WriteAllBytes(ConfigPath, []);
                break;
            case SettingsFileDamage.TruncatedWithBackup:
            case SettingsFileDamage.TruncatedWithoutBackup:
                File.WriteAllText(ConfigPath, "{\"TaskStorage\":", Encoding.UTF8);
                break;
            case SettingsFileDamage.InvalidRootWithoutBackup:
                File.WriteAllText(ConfigPath, "[]", Encoding.UTF8);
                break;
            case SettingsFileDamage.MissingWithInvalidBackup:
                File.WriteAllText(BackupPath, "{", Encoding.UTF8);
                File.Delete(ConfigPath);
                break;
            case SettingsFileDamage.MissingWithBackup:
                File.Delete(ConfigPath);
                break;
            case SettingsFileDamage.ValidWithRestrictedBackup:
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("The restricted-backup fixture requires Windows DACL support.");
                }
                File.Copy(ConfigPath, BackupPath);
                RestrictToCurrentUser(BackupPath);
                break;
        }

        InitialMain = ReadOptional(ConfigPath);
        InitialBackup = ReadOptional(BackupPath);
        InitialTaskFiles = TaskFilesFingerprint();
    }

    public string ConfigPath { get; }
    public string BackupPath => ConfigPath + ".bak";
    public string TasksPath { get; }
    public byte[]? InitialMain { get; }
    public byte[]? InitialBackup { get; }
    public string InitialTaskFiles { get; }

    // Session disposal closes the process. Fixture disposal removes the profile only after all restarts.
    public DesktopAppLaunchOptions LaunchOptions => new()
    {
        ExecutablePath = string.IsNullOrWhiteSpace(_publishedExecutable) ? _ownedOptions.ExecutablePath : _publishedExecutable,
        WorkingDirectory = string.IsNullOrWhiteSpace(_publishedExecutable) ? _ownedOptions.WorkingDirectory : Path.GetDirectoryName(_publishedExecutable),
        Arguments = _ownedOptions.Arguments,
        EnvironmentVariables = CreateEnvironmentVariables(),
        MainWindowTimeout = _ownedOptions.MainWindowTimeout,
        PollInterval = _ownedOptions.PollInterval,
        WindowPlacement = DesktopWindowPlacement.Centered(1000, 700)
    };

    private Dictionary<string, string?> CreateEnvironmentVariables()
    {
        var variables = _ownedOptions.EnvironmentVariables.ToDictionary(pair => pair.Key, pair => pair.Value);
        variables[UnlimotionAppLaunchHost.AutomationWindowTitleEnvironmentVariable] = _windowTitle;
        return variables;
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUser(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("Current Windows user has no SID.");
        var permissions = new FileSecurity();
        permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        permissions.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(permissions);
    }

    public string[] CorruptCopies() => Directory.GetFiles(
        Path.GetDirectoryName(ConfigPath)!, Path.GetFileName(ConfigPath) + ".corrupt-*");

    public string TaskFilesFingerprint() => string.Join("\n",
        Directory.EnumerateFiles(TasksPath, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(TasksPath, path) + ":" +
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));

    public static byte[]? ReadOptional(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    public void Dispose() => _ownedOptions.DisposeCallback?.Invoke();
}
