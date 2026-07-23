using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class CiReadmeMediaContract
{
    public static async Task<CiReadmeMediaScenarioResult> ExecuteAsync()
    {
        var workflow = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath(".github/workflows/tests.yml"));
        var androidWorkflow = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath(".github/workflows/android-packaging.yml"));
        var debWorkflow = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath(".github/workflows/deb_packaging.yml"));
        var nugetSignatureScript = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("scripts/Test-NuGetSignatureChain.ps1"));
        var nugetBaselineFixture = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("distribution/fixtures/reactiveui-signature-chain-baseline.json"));
        var mediaScript = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("scripts/update-readme-media.ps1"));
        var mediaReadme = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("tests/Unlimotion.ReadmeMedia/README.md"));
        var readmeDemoTests = await File.ReadAllTextAsync(
            PlatformShellProjectContracts.GetRepositoryPath("tests/Unlimotion.UiTests.Headless/Tests/ReadmeDemoHeadlessTests.cs"));

        await Assert.That(workflow).Contains("all-tests:");
        await Assert.That(workflow).Contains("name: Regression");
        await Assert.That(workflow).Contains("runs-on: windows-2022");
        await Assert.That(workflow).Contains("timeout-minutes: 120");
        await Assert.That(workflow).Contains("DOTNET_NUGET_SIGNATURE_VERIFICATION: \"true\"");
        await Assert.That(workflow).Contains("persist-credentials: false");
        await Assert.That(workflow).Contains("submodules: false");
        await Assert.That(workflow).Contains("Run and validate Signature evidence");
        await Assert.That(workflow).Contains("Export-VerifiedGitBlob");
        await Assert.That(workflow).Contains("Test-NuGetEvidencePublication.ps1");
        await Assert.That(workflow).Contains("safe_upload_verified");
        await Assert.That(workflow).Contains("${RepositoryPath}:");
        await Assert.That(workflow).Contains("Enforce Signature attempt verdict");
        await Assert.That(workflow).Contains("Run and validate Regression evidence");
        await Assert.That(workflow).Contains("EXPECTED_LANE: Regression");
        await Assert.That(workflow).Contains("Upload Regression evidence");
        await Assert.That(workflow).Contains("Enforce Regression attempt verdict");
        await Assert.That(nugetSignatureScript).Contains("tests\\Unlimotion.UiTests.Headless\\Unlimotion.UiTests.Headless.csproj");
        await Assert.That(nugetSignatureScript).Contains("--maximum-parallel-tests");
        await Assert.That(nugetSignatureScript).Contains("regression:test:headless-1");
        await Assert.That(nugetSignatureScript).Contains("regression:test:headless-2");
        await Assert.That(nugetSignatureScript).Contains("GenerateBaseline");
        await Assert.That(nugetSignatureScript).Contains("ExpectedParentSha");
        await Assert.That(nugetSignatureScript).Contains("Get-CanonicalGraphHash");
        await Assert.That(nugetSignatureScript).Contains("Assert-CandidateGraphsAgainstBaseline");
        await Assert.That(nugetSignatureScript).Contains("Assert-GraphDiffIsApproved");
        await Assert.That(nugetSignatureScript).Contains("Read-ClosedWorkerFrame");
        await Assert.That(nugetSignatureScript).Contains("([int]$header[2]) -shl 8");
        await Assert.That(nugetSignatureScript).Contains("multi-byte length prefix");
        await Assert.That(nugetSignatureScript).Contains("Invoke-ClosedWorkerCliMode");
        await Assert.That(nugetSignatureScript).Contains("Invoke-ClosedWorkerProcessAdapter");
        await Assert.That(nugetSignatureScript).Contains("Get-ClosedSecretSeedSnapshot");
        await Assert.That(nugetSignatureScript).Contains("Test-SecretEnvironmentName");
        await Assert.That(nugetSignatureScript).Contains("TerminationProven");
        await Assert.That(nugetSignatureScript).Contains("native-output-limit-exceeded");
        await Assert.That(nugetSignatureScript).Contains("SignatureVerify");
        using var nugetBaselineDocument = JsonDocument.Parse(nugetBaselineFixture);
        var nugetBaseline = nugetBaselineDocument.RootElement;
        var baselineProjects = nugetBaseline.GetProperty("projects");
        await Assert.That(nugetBaseline.GetProperty("sourceSha").GetString()).IsEqualTo("e11cae9a086ddd4fd97105f00b67bedf05f92700");
        await Assert.That(nugetBaseline.GetProperty("inputManifest").GetArrayLength()).IsEqualTo(5);
        await Assert.That(baselineProjects.GetArrayLength()).IsEqualTo(3);
        await Assert.That(baselineProjects[0].GetProperty("projectPath").GetString()).IsEqualTo("tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj");
        await Assert.That(baselineProjects[1].GetProperty("projectPath").GetString()).IsEqualTo("src/Unlimotion.Desktop/Unlimotion.Desktop.csproj");
        await Assert.That(baselineProjects[2].GetProperty("projectPath").GetString()).IsEqualTo("src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj");

        await Assert.That(androidWorkflow).Contains("android-build:");
        await Assert.That(androidWorkflow).Contains("android-release:");
        await Assert.That(androidWorkflow).Contains("apk_artifact_digest");
        await Assert.That(androidWorkflow).Contains("actions: read");
        await Assert.That(androidWorkflow).Contains("contents: write");
        await Assert.That(androidWorkflow).Contains("APK artifact archive digest mismatch.");
        await Assert.That(androidWorkflow).Contains("/actions/artifacts/$ARTIFACT_ID/zip");

        await Assert.That(debWorkflow).Contains("deb-build:");
        await Assert.That(debWorkflow).Contains("deb-release:");
        await Assert.That(debWorkflow).Contains("release_artifact_digest");
        await Assert.That(debWorkflow).Contains("Linux release artifact archive digest mismatch.");
        await Assert.That(debWorkflow).Contains("/actions/artifacts/$ARTIFACT_ID/zip");

        await Assert.That(mediaScript).Contains("tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj");
        await Assert.That(mediaScript).Contains("tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj");
        await Assert.That(mediaScript).Contains("tests/Unlimotion.ReadmeMedia/Unlimotion.ReadmeMedia.csproj");
        await Assert.That(mediaScript).Contains("--copy-to-media");
        await Assert.That(mediaScript).Contains("[string]$Languages = \"en,ru\"");

        await Assert.That(mediaReadme).Contains("scripts/update-readme-media.ps1");
        await Assert.That(mediaReadme).Contains("runs the headless and FlaUI UI tests sequentially");
        await Assert.That(mediaReadme).Contains("ReadmeDemo");
        await Assert.That(mediaReadme).Contains("English and Russian variants");
        await Assert.That(readmeDemoTests).Contains("ReadmeDemoEnglishHeadlessTests");
        await Assert.That(readmeDemoTests).Contains("ReadmeDemoRussianHeadlessTests");
        await Assert.That(readmeDemoTests).Contains("Readme_demo_uses_capture_presentation_state");

        var loadingTests = new MainScreenLoadingUiTests();
        await loadingTests.MainScreen_Connect_KeepsUiResponsive_DuringBlockingInitialLoad();

        return new CiReadmeMediaScenarioResult
        {
            CiSmokeContractPassed = true,
            ReadmeMediaAutomationContractPassed = true,
            UiResponsiveSmokePassed = true
        };
    }

    public static async Task AssertAsync(CiReadmeMediaScenarioResult result)
    {
        await Assert.That(result.CiSmokeContractPassed).IsTrue();
        await Assert.That(result.ReadmeMediaAutomationContractPassed).IsTrue();
        await Assert.That(result.UiResponsiveSmokePassed).IsTrue();
    }
}

internal sealed class CiReadmeMediaScenarioResult
{
    public bool CiSmokeContractPassed { get; set; }

    public bool ReadmeMediaAutomationContractPassed { get; set; }

    public bool UiResponsiveSmokePassed { get; set; }
}
