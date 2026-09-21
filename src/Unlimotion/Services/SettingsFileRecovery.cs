using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Extensions.Configuration.Json;

namespace Unlimotion.Services;

internal enum SettingsRecoveryStatus { Ready, Restored, Blocked }
internal enum SettingsRecoveryError { None, InvalidSettings, AccessDenied, IoFailure }
internal enum SettingsRecoveryOperation { Backup, Preserve, Restore, SetPermissions, Write, Publish }

internal sealed record SettingsFileRecoveryResult(
    SettingsRecoveryStatus Status,
    string ConfigPath,
    SettingsRecoveryError Error = SettingsRecoveryError.None,
    string? PreservedPath = null);

/// <summary>Runs before any settings consumer; never replaces corruption with defaults.</summary>
[SupportedOSPlatform("windows")]
internal static class SettingsFileRecovery
{
    internal static SettingsFileRecoveryResult Prepare(
        string physicalPath,
        Action<SettingsRecoveryOperation, string>? beforeOperation = null)
    {
        var path = Path.GetFullPath(physicalPath);
        string? preservedPath = null;
        try
        {
            var main = ReadOptional(path);
            var backupPath = path + ".bak";
            var backup = ReadOptional(backupPath);

            if (main != null && IsReadableConfiguration(main))
            {
                if (backup == null)
                {
                    beforeOperation?.Invoke(SettingsRecoveryOperation.Backup, backupPath);
                    PublishCopy(main, path, backupPath, replace: false, beforeOperation);
                }
                else
                {
                    // Runtime initialization also saves defaults. Reject an incompatible backup
                    // here, before those writes can bypass the early recovery shell.
                    EnsureCompatibleAccess(path, backupPath);
                }

                return new(SettingsRecoveryStatus.Ready, path);
            }

            if (main == null && backup == null)
            {
                return new(SettingsRecoveryStatus.Ready, path);
            }

            if (backup == null || !IsReadableConfiguration(backup))
            {
                return new(SettingsRecoveryStatus.Blocked, path, SettingsRecoveryError.InvalidSettings);
            }

            if (main != null)
            {
                preservedPath = path + $".corrupt-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}";
                beforeOperation?.Invoke(SettingsRecoveryOperation.Preserve, preservedPath);
                WriteNew(main, path, preservedPath, beforeOperation);
            }

            beforeOperation?.Invoke(SettingsRecoveryOperation.Restore, path);
            // No backup argument: replacing main must not poison the good .bak with corruption.
            PublishCopy(backup, backupPath, path, replace: main != null, beforeOperation);
            return new(SettingsRecoveryStatus.Restored, path, PreservedPath: preservedPath);
        }
        catch (UnauthorizedAccessException)
        {
            return new(SettingsRecoveryStatus.Blocked, path, SettingsRecoveryError.AccessDenied, preservedPath);
        }
        catch (IOException)
        {
            return new(SettingsRecoveryStatus.Blocked, path, SettingsRecoveryError.IoFailure, preservedPath);
        }
    }

    internal static bool IsReadableConfiguration(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            // This is the same parser used by WritableJsonConfiguration's base provider.
            new JsonConfigurationProvider(new JsonConfigurationSource()).Load(stream);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[]? ReadOptional(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        // File.Exists masks access errors; those must block startup, not masquerade as first run.
    }

    private static void PublishCopy(
        byte[] bytes,
        string source,
        string destination,
        bool replace,
        Action<SettingsRecoveryOperation, string>? beforeOperation)
    {
        if (replace)
        {
            EnsureCompatibleAccess(source, destination);
        }

        var temporaryPath = destination + $".tmp-{Guid.NewGuid():N}";
        try
        {
            WriteNew(bytes, source, temporaryPath, beforeOperation);
            beforeOperation?.Invoke(SettingsRecoveryOperation.Publish, destination);
            if (replace)
            {
                File.Replace(temporaryPath, destination, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, destination);
            }
        }
        finally
        {
            DeleteOwnIncompleteCopy(temporaryPath);
        }
    }

    private static FileSecurity RestrictedPermissions(string path)
    {
        var permissions = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        return permissions;
    }

    private static void EnsureCompatibleAccess(string source, string destination)
    {
        // Compare access rather than inherited/protected provenance; no ACL merging.
        if (AccessFingerprint(RestrictedPermissions(source)) != AccessFingerprint(RestrictedPermissions(destination)))
        {
            throw new UnauthorizedAccessException("Settings recovery cannot preserve both files' access restrictions.");
        }
    }

    private static string AccessFingerprint(FileSecurity permissions)
    {
        var dacl = new RawSecurityDescriptor(permissions.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl;
        if (dacl == null) return "null-dacl";
        var result = new StringBuilder();
        var group = new List<string>();
        var previousType = -1;
        for (var index = 0; index < dacl.Count; index++)
        {
            var ace = dacl[index];
            // Windows removes inherited provenance and reorders adjacent allow ACEs when
            // persisting a protected copy. Never reorder across an allow/deny boundary.
            var canReorder = ace is CommonAce { IsCallback: false } common &&
                             common.AceQualifier is AceQualifier.AccessAllowed or AceQualifier.AccessDenied;
            var type = canReorder ? (int)ace.AceType : 256 + index;
            if (type != previousType)
            {
                AppendAccessGroup(result, group, previousType);
                previousType = type;
            }
            var bytes = new byte[ace.BinaryLength];
            ace.GetBinaryForm(bytes, 0);
            var normalized = GenericAce.CreateFromBinaryForm(bytes, 0);
            if (canReorder) normalized.AceFlags &= ~AceFlags.Inherited;
            normalized.GetBinaryForm(bytes, 0);
            group.Add(Convert.ToBase64String(bytes));
        }
        AppendAccessGroup(result, group, previousType);
        return result.ToString();
    }

    private static void AppendAccessGroup(StringBuilder result, List<string> group, int type)
    {
        if (group.Count == 0) return;
        group.Sort(StringComparer.Ordinal);
        result.Append(type).Append(':').Append(string.Join(",", group)).Append(';');
        group.Clear();
    }

    private static void WriteNew(
        byte[] bytes,
        string source,
        string destination,
        Action<SettingsRecoveryOperation, string>? beforeOperation)
    {
        var permissions = RestrictedPermissions(source);
        var created = false;
        try
        {
            using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            beforeOperation?.Invoke(SettingsRecoveryOperation.SetPermissions, destination);
            new FileInfo(destination).SetAccessControl(permissions);
            beforeOperation?.Invoke(SettingsRecoveryOperation.Write, destination);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            if (created)
            {
                DeleteOwnIncompleteCopy(destination);
            }
            throw;
        }
    }

    private static void DeleteOwnIncompleteCopy(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
