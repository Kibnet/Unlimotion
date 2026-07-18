using System.IO;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class DesktopShellPackagingContract
{
    public static async Task<DesktopShellPackagingScenarioResult> ExecuteAsync()
    {
        var desktopProject = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("src/Unlimotion.Desktop/Unlimotion.Desktop.csproj"));
        var packagingWorkflow = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath(".github/workflows/windows-packaging.yml"));

        await Assert.That(desktopProject).Contains("<OutputType>WinExe</OutputType>");
        await Assert.That(desktopProject).Contains("PackageReference Include=\"Avalonia.Desktop\"");
        await Assert.That(desktopProject).Contains("PackageReference Include=\"Velopack\"");
        await Assert.That(packagingWorkflow).Contains("dotnet publish src\\Unlimotion.Desktop\\Unlimotion.Desktop.csproj");
        await Assert.That(packagingWorkflow).Contains("vpk pack");
        await Assert.That(packagingWorkflow).Contains("--mainExe Unlimotion.Desktop.exe");

        var startupTests = new SingleViewStartupUiTests();
        var packageTests = new PackageUpdateCompatibilityUiTests();
        await startupTests.SingleViewStartup_ConnectsExistingTaskStorage();
        await startupTests.SingleViewStartup_ReplaysStartupUpdateCheck_WhenUpdateServiceAttachesAfterStartup();
        await packageTests.RoadmapDropAndFolderPickerCompatibility_Work();

        return new DesktopShellPackagingScenarioResult
        {
            DesktopPackagingContractPassed = true,
            StartupAndUpdatePassed = true,
            PackageCompatibilityUiPassed = true
        };
    }

    public static async Task AssertAsync(DesktopShellPackagingScenarioResult result)
    {
        await Assert.That(result.DesktopPackagingContractPassed).IsTrue();
        await Assert.That(result.StartupAndUpdatePassed).IsTrue();
        await Assert.That(result.PackageCompatibilityUiPassed).IsTrue();
    }
}

internal sealed class DesktopShellPackagingScenarioResult
{
    public bool DesktopPackagingContractPassed { get; set; }

    public bool StartupAndUpdatePassed { get; set; }

    public bool PackageCompatibilityUiPassed { get; set; }
}
