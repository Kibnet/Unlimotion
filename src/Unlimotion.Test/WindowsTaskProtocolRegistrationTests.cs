using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unlimotion.Desktop.Services;
using Velopack.Locators;

namespace Unlimotion.Test;

public class WindowsTaskProtocolRegistrationTests
{
    [Test]
    public async Task InstalledLayout_ResolvesExistingContentExecutableInsteadOfMissingRootLauncher()
    {
        var root = Path.Combine(Path.GetTempPath(), "Unlimotion protocol пробел", Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "current");
        Directory.CreateDirectory(content);
        var executable = Path.Combine(content, "Unlimotion.Desktop.exe");
        await File.WriteAllTextAsync(executable, "installed executable fixture");
        try
        {
            var locator = new TestVelopackLocator("Unlimotion", "1.0.0", Path.Combine(root, "packages"),
                content, root, Path.Combine(root, "Update.exe"), processPath: executable);

            var resolved = WindowsTaskProtocolRegistrar.ResolveInstalledLauncherPath(locator);

            await Assert.That(File.Exists(Path.Combine(root, "Unlimotion.Desktop.exe"))).IsFalse();
            await Assert.That(resolved).IsEqualTo(executable);
            await Assert.That(File.Exists(resolved)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Registration_QuotesStableLauncherAndUriArgument()
    {
        var registry = new RecordingRegistry();
        var service = new WindowsTaskProtocolRegistrationService(registry);

        service.Register(@"C:\Users\User Name\AppData\Local\Unlimotion\Unlimotion.Desktop.exe");

        await Assert.That(registry.Values[(WindowsTaskProtocolRegistrationService.ProtocolKey, "URL Protocol")])
            .IsEqualTo(string.Empty);
        await Assert.That(registry.Values[(WindowsTaskProtocolRegistrationService.ProtocolKey + @"\shell\open\command", null)])
            .IsEqualTo("\"C:\\Users\\User Name\\AppData\\Local\\Unlimotion\\Unlimotion.Desktop.exe\" \"%1\"");
    }

    [Test]
    public async Task Unregister_DeletesOnlyRegistrationOwnedBySameLauncher()
    {
        var registry = new RecordingRegistry();
        var service = new WindowsTaskProtocolRegistrationService(registry);
        const string launcher = @"C:\Apps\Unlimotion.Desktop.exe";
        service.Register(launcher);

        await Assert.That(service.Unregister(@"C:\Other\Unlimotion.Desktop.exe")).IsFalse();
        await Assert.That(registry.DeletedTrees).IsEmpty();
        await Assert.That(service.Unregister(launcher)).IsTrue();
        await Assert.That(registry.DeletedTrees).IsEquivalentTo([WindowsTaskProtocolRegistrationService.ProtocolKey]);
    }

    [Test]
    public async Task InstalledRegistration_ReplacesLegacyValuesAndNotifiesAfterCompleteWrite()
    {
        using var installation = new InstallationFixture();
        var registry = new RecordingRegistry();
        new WindowsTaskProtocolRegistrationService(registry).Register(installation.LegacyExecutable);
        registry.Events.Clear();
        var notifications = 0;

        void Notify()
        {
            if (registry.Values[(WindowsTaskProtocolRegistrationService.ProtocolKey + @"\shell\open\command", null)]
                != WindowsTaskProtocolRegistrationService.BuildOpenCommand(installation.Executable))
                throw new InvalidOperationException("Notification arrived before the executable command was written.");
            registry.Events.Add("notify");
            notifications++;
        }

        WindowsTaskProtocolRegistrar.RegisterInstalledApplication(installation.Locator, registry, Notify);
        WindowsTaskProtocolRegistrar.RegisterInstalledApplication(installation.Locator, registry, Notify);

        await Assert.That(registry.Values[(WindowsTaskProtocolRegistrationService.ProtocolKey, "UnlimotionOwner")])
            .IsEqualTo(installation.Executable);
        await Assert.That(registry.Values[(WindowsTaskProtocolRegistrationService.ProtocolKey + @"\DefaultIcon", null)])
            .IsEqualTo($"\"{installation.Executable}\",0");
        await Assert.That(registry.Values.Count).IsEqualTo(5);
        await Assert.That(notifications).IsEqualTo(2);
        await Assert.That(registry.Events[^1]).IsEqualTo("notify");
    }

    [Test]
    public async Task MissingInstalledExecutable_DoesNotWriteOrNotify()
    {
        using var installation = new InstallationFixture();
        File.Delete(installation.Executable);
        var registry = new RecordingRegistry();
        var notifications = 0;

        await Assert.That(() => WindowsTaskProtocolRegistrar.RegisterInstalledApplication(
            installation.Locator, registry, () => notifications++)).Throws<FileNotFoundException>();

        await Assert.That(registry.Values).IsEmpty();
        await Assert.That(registry.Events).IsEmpty();
        await Assert.That(notifications).IsEqualTo(0);
    }

    [Test]
    public async Task RegistryWriteFailure_DoesNotSendSuccessNotification()
    {
        using var installation = new InstallationFixture();
        var registry = new RecordingRegistry { FailCommandWrite = true };
        var notifications = 0;

        await Assert.That(() => WindowsTaskProtocolRegistrar.RegisterInstalledApplication(
            installation.Locator, registry, () => notifications++)).Throws<IOException>();

        await Assert.That(notifications).IsEqualTo(0);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("relative/current")]
    public async Task InvalidContentDirectory_IsRejected(string? directory)
    {
        await Assert.That(() => WindowsTaskProtocolRegistrar.ResolveInstalledLauncherPath(
            directory, "Unlimotion.Desktop.exe", requireExecutable: false)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("../outside.exe")]
    [Arguments("../current-other/outside.exe")]
    [Arguments(".")]
    public async Task InvalidRelativeExecutable_IsRejected(string? relativePath)
    {
        using var installation = new InstallationFixture();
        await Assert.That(() => WindowsTaskProtocolRegistrar.ResolveInstalledLauncherPath(
            installation.ContentDirectory, relativePath, requireExecutable: false)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AbsoluteExecutablePath_IsRejectedEvenWhenItExists()
    {
        using var installation = new InstallationFixture();
        await Assert.That(() => WindowsTaskProtocolRegistrar.ResolveInstalledLauncherPath(
            installation.ContentDirectory, installation.Executable)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NestedExecutable_UsesRelativeContentPath()
    {
        using var installation = new InstallationFixture();
        var nested = Path.Combine(installation.ContentDirectory, "bin", "App.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        await File.WriteAllTextAsync(nested, "nested executable fixture");

        var actual = WindowsTaskProtocolRegistrar.ResolveInstalledLauncherPath(
            installation.ContentDirectory, Path.Combine("bin", "App.exe"));

        await Assert.That(actual).IsEqualTo(nested);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Uninstall_CleansOwnCurrentOrLegacyRegistrationAfterExecutableIsRemoved(bool legacy)
    {
        using var installation = new InstallationFixture();
        var registry = new RecordingRegistry();
        new WindowsTaskProtocolRegistrationService(registry)
            .Register(legacy ? installation.LegacyExecutable : installation.Executable);
        File.Delete(installation.Executable);
        registry.Events.Clear();

        var removed = WindowsTaskProtocolRegistrar.UnregisterInstalledApplication(
            installation.Locator, registry, () => registry.Events.Add("notify"));

        await Assert.That(removed).IsTrue();
        await Assert.That(registry.Events).IsEquivalentTo(["delete", "notify"]);
        await Assert.That(registry.Values).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Uninstall_PreservesChangedCommandEvenWhenOwnerMatches(bool legacy)
    {
        using var installation = new InstallationFixture();
        var registry = new RecordingRegistry();
        new WindowsTaskProtocolRegistrationService(registry)
            .Register(legacy ? installation.LegacyExecutable : installation.Executable);
        registry.Values[(WindowsTaskProtocolRegistrationService.ProtocolKey + @"\shell\open\command", null)] = "custom handler";
        registry.Events.Clear();

        var removed = WindowsTaskProtocolRegistrar.UnregisterInstalledApplication(
            installation.Locator, registry, () => registry.Events.Add("notify"));

        await Assert.That(removed).IsFalse();
        await Assert.That(registry.Events).IsEmpty();
        await Assert.That(registry.Values.Count).IsEqualTo(5);
    }

    [Test]
    public async Task Uninstall_PreservesForeignOwnerAndDoesNotNotify()
    {
        using var installation = new InstallationFixture();
        var registry = new RecordingRegistry();
        new WindowsTaskProtocolRegistrationService(registry).Register(Path.Combine(installation.Root, "other", "App.exe"));
        registry.Events.Clear();

        var removed = WindowsTaskProtocolRegistrar.UnregisterInstalledApplication(
            installation.Locator, registry, () => registry.Events.Add("notify"));

        await Assert.That(removed).IsFalse();
        await Assert.That(registry.Events).IsEmpty();
    }

    private sealed class InstallationFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Unlimotion protocol пробел", Guid.NewGuid().ToString("N"));
        public string ContentDirectory => Path.Combine(Root, "current");
        public string Executable => Path.Combine(ContentDirectory, "Unlimotion.Desktop.exe");
        public string LegacyExecutable => Path.Combine(Root, "Unlimotion.Desktop.exe");
        public TestVelopackLocator Locator { get; }

        public InstallationFixture()
        {
            Directory.CreateDirectory(ContentDirectory);
            File.WriteAllText(Executable, "installed executable fixture");
            Locator = new TestVelopackLocator("Unlimotion", "1.0.0", Path.Combine(Root, "packages"),
                ContentDirectory, Root, Path.Combine(Root, "Update.exe"), processPath: Executable);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class RecordingRegistry : IWindowsProtocolRegistry
    {
        public Dictionary<(string Key, string? Name), string> Values { get; } = [];
        public List<string> DeletedTrees { get; } = [];
        public List<string> Events { get; } = [];
        public bool FailCommandWrite { get; init; }

        public string? Read(string subKey, string? name) => Values.GetValueOrDefault((subKey, name));
        public void Write(string subKey, string? name, string value)
        {
            if (FailCommandWrite && subKey.EndsWith(@"\command", StringComparison.Ordinal))
                throw new IOException("Injected registry write failure.");
            Values[(subKey, name)] = value;
            Events.Add("write");
        }
        public void DeleteTree(string subKey)
        {
            DeletedTrees.Add(subKey);
            Values.Clear();
            Events.Add("delete");
        }
    }
}
