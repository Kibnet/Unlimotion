using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Unlimotion.ViewModel;

public static class SshKeyStoragePathResolver
{
    public static string GetSshDirectory(GitSettings? gitSettings = null)
    {
        return ResolveSshDirectory(gitSettings?.SshKeyStoragePath);
    }

    public static string ResolveSshDirectory(string? configuredPath)
    {
        return string.IsNullOrWhiteSpace(configuredPath)
            ? GetDefaultSshDirectory()
            : Path.GetFullPath(configuredPath);
    }

    public static string GetDefaultSshDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(profile, ".ssh");
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(home, ".ssh");
    }
}
