using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Unlimotion.Test;

public class TaskDeepLinkPackagingContractTests
{
    [Test]
    public async Task DesktopStartup_RegistersAndRemovesProtocolThroughVelopackLifecycle()
    {
        var program = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("src/Unlimotion.Desktop/Program.cs"));

        await Assert.That(program).Contains("OnAfterInstallFastCallback");
        await Assert.That(program).Contains("OnAfterUpdateFastCallback");
        await Assert.That(program).Contains("OnBeforeUninstallFastCallback");
        await Assert.That(program).Contains("WindowsTaskProtocolRegistrar.RegisterInstalledApplication()");
        await Assert.That(program).Contains("WindowsTaskProtocolRegistrar.UnregisterInstalledApplication()");
    }

    [Test]
    public async Task MacStartup_UsesProtocolLifetimeInsteadOfArgumentBroker()
    {
        var program = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("src/Unlimotion.Desktop/Program.cs"));

        await Assert.That(program).Contains("var usesArgumentTaskDeepLinks = !OperatingSystem.IsMacOS();");
    }

    [Test]
    public async Task DesktopBroker_UsesWindowsMutexNamespaceOnlyOnWindows()
    {
        var broker = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath(
                "src/Unlimotion.Desktop/Services/TaskDeepLinkActivationBroker.cs"));

        await Assert.That(broker)
            .Contains("GetOwnershipName(resolvedChannelName, OperatingSystem.IsWindows())");
    }

    [Test]
    public async Task LinuxDesktopEntry_RegistersTaskProtocolAndForwardsOneUri()
    {
        var desktopEntry = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("src/Unlimotion.Desktop/ci/deb/unlimotion.desktop"));

        await Assert.That(desktopEntry).Contains("MimeType=x-scheme-handler/unlimotion;");
        await Assert.That(desktopEntry).Contains("Exec=/usr/bin/Unlimotion %u");
    }

    [Test]
    public async Task MacBundle_RegistersUnlimotionUrlScheme()
    {
        var plist = XDocument.Load(
            PlatformShellProjectContracts.GetRepositoryPath("src/Unlimotion.Desktop/ci/osx/Info.plist"));
        var strings = plist.Descendants("string").Select(static value => value.Value).ToArray();

        await Assert.That(plist.Descendants("key").Select(static key => key.Value))
            .Contains("CFBundleURLTypes");
        await Assert.That(strings).Contains("unlimotion");
    }

    [Test]
    public async Task AndroidActivity_RegistersBrowsableTaskProtocolIntent()
    {
        var mainActivity = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("src/Unlimotion.Android/MainActivity.cs"));

        await Assert.That(mainActivity).Contains("Intent.ActionView");
        await Assert.That(mainActivity).Contains("Intent.CategoryDefault");
        await Assert.That(mainActivity).Contains("Intent.CategoryBrowsable");
        await Assert.That(mainActivity).Contains("DataScheme = TaskDeepLink.Scheme");
        await Assert.That(mainActivity).Contains("DataHost = TaskDeepLink.TaskHost");
    }
}
