using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Velopack.Locators;
using Velopack.Logging;

[assembly: InternalsVisibleTo("Unlimotion.Test")]

namespace Unlimotion.Desktop.Services;

public interface IWindowsProtocolRegistry
{
    string? Read(string subKey, string? name);
    void Write(string subKey, string? name, string value);
    void DeleteTree(string subKey);
}

public sealed class WindowsTaskProtocolRegistrationService(
    IWindowsProtocolRegistry registry, Action? associationsChanged = null)
{
    public const string ProtocolKey = @"Software\Classes\unlimotion";
    public const string OwnerValueName = "UnlimotionOwner";

    public void Register(string launcherPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherPath);
        var fullPath = Path.GetFullPath(launcherPath);
        registry.Write(ProtocolKey, null, "URL:Unlimotion Task Protocol");
        registry.Write(ProtocolKey, "URL Protocol", string.Empty);
        registry.Write(ProtocolKey, OwnerValueName, fullPath);
        registry.Write($@"{ProtocolKey}\DefaultIcon", null, $"\"{fullPath}\",0");
        registry.Write($@"{ProtocolKey}\shell\open\command", null, BuildOpenCommand(fullPath));
        associationsChanged?.Invoke();
    }

    public bool Unregister(string launcherPath, string? legacyLauncherPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherPath);
        var expectedOwner = Path.GetFullPath(launcherPath);
        var registeredOwner = registry.Read(ProtocolKey, OwnerValueName);
        var registeredCommand = registry.Read($@"{ProtocolKey}\shell\open\command", null);
        var ownsCurrent = OwnsRegistration(expectedOwner);
        var ownsLegacy = legacyLauncherPath is not null && OwnsRegistration(Path.GetFullPath(legacyLauncherPath));
        if (!ownsCurrent && !ownsLegacy)
        {
            return false;
        }

        registry.DeleteTree(ProtocolKey);
        associationsChanged?.Invoke();
        return true;

        bool OwnsRegistration(string owner) =>
            string.Equals(registeredOwner, owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(registeredCommand, BuildOpenCommand(owner), StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildOpenCommand(string launcherPath) => $"\"{Path.GetFullPath(launcherPath)}\" \"%1\"";
}

public static class WindowsTaskProtocolRegistrar
{
    public static void RegisterInstalledApplication()
    {
        if (!OperatingSystem.IsWindows()) return;
        var locator = VelopackLocator.Current;
        try
        {
            RegisterInstalledApplication(locator, new CurrentUserProtocolRegistry(), NotifyShell);
        }
        catch (Exception exception)
        {
            locator.Log.Log(VelopackLogLevel.Error, "Windows task protocol registration failed.", exception);
            throw;
        }
    }

    public static void UnregisterInstalledApplication()
    {
        if (!OperatingSystem.IsWindows()) return;
        var locator = VelopackLocator.Current;
        try
        {
            UnregisterInstalledApplication(locator, new CurrentUserProtocolRegistry(), NotifyShell);
        }
        catch (Exception exception)
        {
            locator.Log.Log(VelopackLogLevel.Error, "Windows task protocol cleanup failed.", exception);
            throw;
        }
    }

    internal static void RegisterInstalledApplication(
        IVelopackLocator locator, IWindowsProtocolRegistry registry, Action associationsChanged)
    {
        var executable = ResolveInstalledLauncherPath(locator);
        new WindowsTaskProtocolRegistrationService(registry, associationsChanged).Register(executable);
    }

    internal static bool UnregisterInstalledApplication(
        IVelopackLocator locator, IWindowsProtocolRegistry registry, Action associationsChanged)
    {
        // Uninstall can run after the content executable has disappeared.
        var executable = ResolveInstalledLauncherPath(locator, requireExecutable: false);
        var legacyLauncher = string.IsNullOrWhiteSpace(locator.RootAppDir)
            ? null
            : Path.Combine(locator.RootAppDir, Path.GetFileName(executable));
        return new WindowsTaskProtocolRegistrationService(registry, associationsChanged)
            .Unregister(executable, legacyLauncher);
    }

    internal static string ResolveInstalledLauncherPath(IVelopackLocator locator, bool requireExecutable = true) =>
        ResolveInstalledLauncherPath(locator.AppContentDir, locator.ThisExeRelativePath, requireExecutable);

    internal static string ResolveInstalledLauncherPath(
        string? appContentDirectory, string? relativeExecutablePath, bool requireExecutable = true)
    {
        if (string.IsNullOrWhiteSpace(appContentDirectory) || !Path.IsPathFullyQualified(appContentDirectory))
        {
            throw new InvalidOperationException("Velopack did not provide an absolute application content directory.");
        }
        if (string.IsNullOrWhiteSpace(relativeExecutablePath) || Path.IsPathRooted(relativeExecutablePath))
        {
            throw new InvalidOperationException("Velopack did not provide a relative application executable path.");
        }

        var contentDirectory = Path.GetFullPath(appContentDirectory);
        var executable = Path.GetFullPath(Path.Combine(contentDirectory, relativeExecutablePath));
        var contentPrefix = Path.EndsInDirectorySeparator(contentDirectory)
            ? contentDirectory : contentDirectory + Path.DirectorySeparatorChar;
        if (!executable.StartsWith(contentPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Application executable '{executable}' is outside '{contentDirectory}'.");
        }
        if (requireExecutable && !File.Exists(executable))
        {
            throw new FileNotFoundException("The installed protocol executable does not exist.", executable);
        }
        return executable;
    }

    [SupportedOSPlatform("windows")]
    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    [SupportedOSPlatform("windows")]
    private sealed class CurrentUserProtocolRegistry : IWindowsProtocolRegistry
    {
        public string? Read(string subKey, string? name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) as string;
        }

        public void Write(string subKey, string? name, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true)
                ?? throw new InvalidOperationException($"Unable to create protocol registry key '{subKey}'.");
            key.SetValue(name, value, RegistryValueKind.String);
        }

        public void DeleteTree(string subKey) => Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }
}
