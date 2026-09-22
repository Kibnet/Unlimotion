using System.IO;
using System.Threading.Tasks;

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
}
