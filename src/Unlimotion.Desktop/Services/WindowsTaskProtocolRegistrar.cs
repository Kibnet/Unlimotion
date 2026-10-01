using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Velopack.Locators;

namespace Unlimotion.Desktop.Services;

public interface IWindowsProtocolRegistry
{
    string? Read(string subKey, string? name);
    void Write(string subKey, string? name, string value);
    void DeleteTree(string subKey);
}

public sealed class WindowsTaskProtocolRegistrationService(IWindowsProtocolRegistry registry)
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
    }

    public bool Unregister(string launcherPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherPath);
        var expectedOwner = Path.GetFullPath(launcherPath);
        var registeredOwner = registry.Read(ProtocolKey, OwnerValueName);
        if (!string.Equals(registeredOwner, expectedOwner, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        registry.DeleteTree(ProtocolKey);
        return true;
    }

    public static string BuildOpenCommand(string launcherPath) => $"\"{Path.GetFullPath(launcherPath)}\" \"%1\"";
}

public static class WindowsTaskProtocolRegistrar
{
    public static void RegisterInstalledApplication()
    {
        if (!OperatingSystem.IsWindows()) return;
        new WindowsTaskProtocolRegistrationService(new CurrentUserProtocolRegistry())
            .Register(ResolveStableLauncherPath());
    }

    public static void UnregisterInstalledApplication()
    {
        if (!OperatingSystem.IsWindows()) return;
        new WindowsTaskProtocolRegistrationService(new CurrentUserProtocolRegistry())
            .Unregister(ResolveStableLauncherPath());
    }

    private static string ResolveStableLauncherPath()
    {
        var locator = VelopackLocator.Current;
        var rootAppDirectory = locator.RootAppDir
            ?? throw new InvalidOperationException("Velopack did not provide the installed application root.");
        var executableName = Path.GetFileName(locator.ProcessExePath);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            throw new InvalidOperationException("Velopack did not provide the application executable path.");
        }

        return Path.Combine(rootAppDirectory, executableName);
    }

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
