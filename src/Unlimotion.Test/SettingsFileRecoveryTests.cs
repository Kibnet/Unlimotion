using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.Services;

namespace Unlimotion.Test;

[SupportedOSPlatform("windows")]
public sealed class SettingsFileRecoveryTests
{
    private const string Settings = "{\"TaskStorage\":{\"Path\":\"original-tasks\"},\"Unknown\":{\"A\":[true,null,3]}}";

    [Before(HookType.Test)]
    public void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Crash-safe settings recovery is supported on Windows only.");
    }

    [Test]
    public async System.Threading.Tasks.Task Valid_main_bootstraps_backup_without_changing_main()
    {
        using var fixture = new Fixture(Settings);
        var original = File.ReadAllBytes(fixture.Main);
        var result = SettingsFileRecovery.Prepare(fixture.Main);
        await Assert.That(result.Status).IsEqualTo(SettingsRecoveryStatus.Ready);
        await Assert.That(File.ReadAllBytes(fixture.Main).SequenceEqual(original)).IsTrue();
        await Assert.That(File.ReadAllBytes(fixture.Backup).SequenceEqual(original)).IsTrue();
        await Assert.That(Directory.GetFiles(fixture.Directory).Length).IsEqualTo(2);
    }

    [Test]
    [Arguments("")]
    [Arguments("{\"TaskStorage\":")]
    [Arguments("[]")]
    [Arguments("null")]
    [Arguments("{\"Key\":1,\"KEY\":2}")]
    public async System.Threading.Tasks.Task Invalid_main_restores_exact_backup_and_preserves_original(string damaged)
    {
        using var fixture = new Fixture(damaged, Settings);
        var result = SettingsFileRecovery.Prepare(fixture.Main);
        await Assert.That(result.Status).IsEqualTo(SettingsRecoveryStatus.Restored);
        await Assert.That(File.ReadAllText(result.PreservedPath!)).IsEqualTo(damaged);
        await Assert.That(File.ReadAllText(fixture.Main)).IsEqualTo(Settings);
        await Assert.That(File.ReadAllText(fixture.Backup)).IsEqualTo(Settings);
        await Assert.That(SettingsFileRecovery.Prepare(fixture.Main).Status).IsEqualTo(SettingsRecoveryStatus.Ready);
        await Assert.That(Directory.GetFiles(fixture.Directory, "*.corrupt-*").Length).IsEqualTo(1);
    }

    [Test]
    public async System.Threading.Tasks.Task Missing_main_uses_backup_not_defaults()
    {
        using var fixture = new Fixture(null, Settings);
        var result = SettingsFileRecovery.Prepare(fixture.Main);
        await Assert.That(result.Status).IsEqualTo(SettingsRecoveryStatus.Restored);
        await Assert.That(result.PreservedPath).IsNull();
        await Assert.That(File.ReadAllText(fixture.Main)).IsEqualTo(Settings);
        await Assert.That(File.ReadAllText(fixture.Backup)).IsEqualTo(Settings);
    }

    [Test]
    public async System.Threading.Tasks.Task Bootstrap_backup_can_restore_main_with_inherited_permissions()
    {
        using var fixture = new Fixture(Settings);
        await Assert.That(SettingsFileRecovery.Prepare(fixture.Main).Status).IsEqualTo(SettingsRecoveryStatus.Ready);
        File.WriteAllText(fixture.Main, "broken");

        var result = SettingsFileRecovery.Prepare(fixture.Main);

        await Assert.That(result.Status).IsEqualTo(SettingsRecoveryStatus.Restored);
        await Assert.That(File.ReadAllText(fixture.Main)).IsEqualTo(Settings);
        await Assert.That(File.ReadAllText(fixture.Backup)).IsEqualTo(Settings);
        await Assert.That(File.ReadAllText(result.PreservedPath!)).IsEqualTo("broken");
    }

    [Test]
    public async System.Threading.Tasks.Task First_run_creates_neither_main_nor_backup()
    {
        using var fixture = new Fixture(null);
        await Assert.That(SettingsFileRecovery.Prepare(fixture.Main).Status).IsEqualTo(SettingsRecoveryStatus.Ready);
        await Assert.That(Directory.GetFiles(fixture.Directory).Length).IsEqualTo(0);
    }

    [Test]
    [Arguments("", null)]
    [Arguments("{", "[]")]
    [Arguments(null, "")]
    public async System.Threading.Tasks.Task No_usable_backup_blocks_without_touching_files(string? main, string? backup)
    {
        using var fixture = new Fixture(main, backup);
        var originalFiles = Directory.GetFiles(fixture.Directory);
        var result = SettingsFileRecovery.Prepare(fixture.Main);
        await Assert.That(result.Status).IsEqualTo(SettingsRecoveryStatus.Blocked);
        await Assert.That(result.Error).IsEqualTo(SettingsRecoveryError.InvalidSettings);
        await Assert.That(Directory.GetFiles(fixture.Directory).SequenceEqual(originalFiles)).IsTrue();
        await Assert.That(File.Exists(fixture.Main) ? File.ReadAllText(fixture.Main) : null).IsEqualTo(main);
        await Assert.That(File.Exists(fixture.Backup) ? File.ReadAllText(fixture.Backup) : null).IsEqualTo(backup);
    }

    [Test]
    public async System.Threading.Tasks.Task Valid_main_does_not_restore_or_delete_damaged_existing_backup()
    {
        using var fixture = new Fixture(Settings, "broken");
        await Assert.That(SettingsFileRecovery.Prepare(fixture.Main).Status).IsEqualTo(SettingsRecoveryStatus.Ready);
        await Assert.That(File.ReadAllText(fixture.Main)).IsEqualTo(Settings);
        await Assert.That(File.ReadAllText(fixture.Backup)).IsEqualTo("broken");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async System.Threading.Tasks.Task Sharing_violation_is_not_corruption_or_missing_file(bool lockBackup)
    {
        using var fixture = new Fixture("", Settings);
        SettingsFileRecoveryResult result;
        using (var locked = new FileStream(lockBackup ? fixture.Backup : fixture.Main,
                   FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = SettingsFileRecovery.Prepare(fixture.Main);
        }

        await Assert.That(result.Status).IsEqualTo(SettingsRecoveryStatus.Blocked);
        await Assert.That(result.Error).IsEqualTo(SettingsRecoveryError.IoFailure);
        await Assert.That(File.ReadAllText(fixture.Main)).IsEqualTo("");
        await Assert.That(File.ReadAllText(fixture.Backup)).IsEqualTo(Settings);
        await Assert.That(Directory.GetFiles(fixture.Directory).Length).IsEqualTo(2);
    }

    [Test]
    [Arguments("Backup")]
    [Arguments("Preserve")]
    [Arguments("Restore")]
    [Arguments("Publish")]
    public async System.Threading.Tasks.Task File_operation_failure_preserves_existing_files(string operationName)
    {
        var failAt = Enum.Parse<SettingsRecoveryOperation>(operationName);
        var bootstrap = failAt == SettingsRecoveryOperation.Backup;
        using var fixture = new Fixture(bootstrap ? Settings : "broken", bootstrap ? null : Settings);
        var result = SettingsFileRecovery.Prepare(fixture.Main, (operation, _) =>
        {
            if (operation == failAt) throw new IOException("Synthetic disk failure");
        });
        await Assert.That(result.Status).IsEqualTo(SettingsRecoveryStatus.Blocked);
        await Assert.That(result.Error).IsEqualTo(SettingsRecoveryError.IoFailure);
        await Assert.That(File.ReadAllText(fixture.Main)).IsEqualTo(bootstrap ? Settings : "broken");
        await Assert.That(File.Exists(fixture.Backup) ? File.ReadAllText(fixture.Backup) : null)
            .IsEqualTo(bootstrap ? null : Settings);
        await Assert.That(Directory.GetFiles(fixture.Directory, "*.tmp-*").Length).IsEqualTo(0);
    }

    [Test]
    public async System.Threading.Tasks.Task Failure_to_set_permissions_never_writes_confidential_bytes()
    {
        using var fixture = new Fixture(Settings);
        var attemptedWrite = false;
        var result = SettingsFileRecovery.Prepare(fixture.Main, (operation, path) =>
        {
            if (operation == SettingsRecoveryOperation.SetPermissions)
            {
                if (new FileInfo(path).Length != 0) throw new InvalidOperationException("Data written before permissions.");
                throw new UnauthorizedAccessException("Synthetic ACL failure");
            }
            if (operation == SettingsRecoveryOperation.Write) attemptedWrite = true;
        });
        await Assert.That(result.Error).IsEqualTo(SettingsRecoveryError.AccessDenied);
        await Assert.That(attemptedWrite).IsFalse();
        await Assert.That(File.ReadAllText(fixture.Main)).IsEqualTo(Settings);
        await Assert.That(File.Exists(fixture.Backup)).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task Copies_preserve_restricted_access_even_with_inheritable_parent_permissions()
    {
        using var fixture = new Fixture(Settings);
        RestrictToCurrentUser(fixture.Main);
        var expected = GetDacl(fixture.Main);
        var bootstrap = SettingsFileRecovery.Prepare(fixture.Main);
        await Assert.That(bootstrap.Status).IsEqualTo(SettingsRecoveryStatus.Ready);
        await Assert.That(GetDacl(fixture.Backup)).IsEqualTo(expected);
        File.WriteAllText(fixture.Main, "broken");
        var restored = SettingsFileRecovery.Prepare(fixture.Main);
        await Assert.That(restored.Status).IsEqualTo(SettingsRecoveryStatus.Restored);
        await Assert.That(GetDacl(fixture.Main)).IsEqualTo(expected);
        await Assert.That(GetDacl(restored.PreservedPath!)).IsEqualTo(expected);
        await Assert.That(GetDacl(fixture.Backup)).IsEqualTo(expected);
    }

    [Test]
    public async System.Threading.Tasks.Task Restore_cannot_expose_a_restricted_backup_through_broader_main()
    {
        using var fixture = new Fixture("broken", Settings);
        RestrictToCurrentUser(fixture.Backup);
        var expected = GetDacl(fixture.Backup);
        var result = SettingsFileRecovery.Prepare(fixture.Main);
        await Assert.That(result.Status).IsEqualTo(SettingsRecoveryStatus.Blocked);
        await Assert.That(result.Error).IsEqualTo(SettingsRecoveryError.AccessDenied);
        await Assert.That(File.ReadAllText(fixture.Main)).IsEqualTo("broken");
        await Assert.That(File.ReadAllText(fixture.Backup)).IsEqualTo(Settings);
        await Assert.That(GetDacl(fixture.Backup)).IsEqualTo(expected);
    }

    [Test]
    public async System.Threading.Tasks.Task Valid_main_with_incompatible_backup_permissions_blocks_before_runtime_writes()
    {
        using var fixture = new Fixture(Settings, Settings);
        RestrictToCurrentUser(fixture.Backup);
        var expected = GetDacl(fixture.Backup);

        var result = SettingsFileRecovery.Prepare(fixture.Main);

        await Assert.That(result.Status).IsEqualTo(SettingsRecoveryStatus.Blocked);
        await Assert.That(result.Error).IsEqualTo(SettingsRecoveryError.AccessDenied);
        await Assert.That(File.ReadAllText(fixture.Main)).IsEqualTo(Settings);
        await Assert.That(File.ReadAllText(fixture.Backup)).IsEqualTo(Settings);
        await Assert.That(GetDacl(fixture.Backup)).IsEqualTo(expected);
        await Assert.That(Directory.GetFiles(fixture.Directory).Length).IsEqualTo(2);
    }

    [Test]
    [Arguments("{ /* comment */ \"Value\": true, }")]
    [Arguments("{\"Value\":null,\"Nested\":{\"Array\":[1,false,\"s\"]}}")]
    public async System.Threading.Tasks.Task Validation_uses_configuration_parser_compatibility(string json)
    {
        await Assert.That(SettingsFileRecovery.IsReadableConfiguration(Encoding.UTF8.GetBytes(json))).IsTrue();
    }

    private static void RestrictToCurrentUser(string path)
    {
        var permissions = new FileSecurity();
        permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        permissions.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(permissions);
    }

    private static string GetDacl(string path) => new FileInfo(path).GetAccessControl(AccessControlSections.Access)
        .GetSecurityDescriptorSddlForm(AccessControlSections.Access);

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "unlimotion-settings-recovery", Guid.NewGuid().ToString("N"));
        public string Main => Path.Combine(Directory, "Settings.json");
        public string Backup => Main + ".bak";

        public Fixture(string? main, string? backup = null)
        {
            System.IO.Directory.CreateDirectory(Directory);
            if (main != null) File.WriteAllText(Main, main);
            if (backup != null) File.WriteAllText(Backup, backup);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
