using System.IO;
using System.Threading.Tasks;

namespace Unlimotion.Test;

public sealed class NuGetCliWorkflowContractTests
{
    [Test]
    public async Task ReleaseTag_IsPassedToPowerShellAsEnvironmentData()
    {
        var workflowPath = PlatformShellProjectContracts.GetRepositoryPath(".github/workflows/nuget-cli.yml");
        var workflow = await File.ReadAllTextAsync(workflowPath);

        await Assert.That(workflow).Contains("RELEASE_TAG: ${{ github.event.release.tag_name }}");
        await Assert.That(workflow).Contains("$tag = $env:RELEASE_TAG");
        await Assert.That(workflow).DoesNotContain("$tag = '${{ github.event.release.tag_name }}'");
    }

    [Test]
    public async Task ReleaseTag_IsTheOnlyCliPackageVersionSource()
    {
        var workflowPath = PlatformShellProjectContracts.GetRepositoryPath(".github/workflows/nuget-cli.yml");
        var projectPath = PlatformShellProjectContracts.GetRepositoryPath("src/Unlimotion.Cli/Unlimotion.Cli.csproj");
        var workflow = await File.ReadAllTextAsync(workflowPath);
        var project = await File.ReadAllTextAsync(projectPath);

        await Assert.That(project).DoesNotContain("<Version>");
        await Assert.That(workflow).DoesNotContain("$projectVersion =");
        await Assert.That(workflow).Contains("-p:PackageVersion=${{ steps.release.outputs.version }}");
    }
}
