using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.Desktop.Services;

namespace Unlimotion.Test;

public class WindowsTaskProtocolRegistrationTests
{
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

    private sealed class RecordingRegistry : IWindowsProtocolRegistry
    {
        public Dictionary<(string Key, string? Name), string> Values { get; } = [];
        public List<string> DeletedTrees { get; } = [];

        public string? Read(string subKey, string? name) => Values.GetValueOrDefault((subKey, name));
        public void Write(string subKey, string? name, string value) => Values[(subKey, name)] = value;
        public void DeleteTree(string subKey) => DeletedTrees.Add(subKey);
    }
}
