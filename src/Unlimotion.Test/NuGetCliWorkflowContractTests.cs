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
}
