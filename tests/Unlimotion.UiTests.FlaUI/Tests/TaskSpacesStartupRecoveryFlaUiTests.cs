using AppAutomation.FlaUI.Session;
using System.Text.Json.Nodes;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class TaskSpacesStartupRecoveryFlaUiTests
{
    [Test]
    [Arguments(UnlimotionAutomationScenario.TaskSpacesDuplicateCatalogRecovery, "default")]
    [Arguments(UnlimotionAutomationScenario.TaskSpacesOrphanCatalogRecovery, "missing-space")]
    [NotInParallel("DesktopUi")]
    public void Corrupt_catalog_opens_safe_recovery_shell(
        UnlimotionAutomationScenario scenario,
        string expectedProblemSourceId)
    {
        var launchOptions = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            scenario,
            buildBeforeLaunch: true,
            mainWindowTimeout: TimeSpan.FromSeconds(90));
        var configPath = launchOptions.Arguments
            .Single(argument => argument.StartsWith("--config=", StringComparison.Ordinal))
            ["--config=".Length..];
        var originalConfig = File.ReadAllText(configPath);
        using var session = DesktopAppSession.Launch(launchOptions);
        session.MainWindow.Focus();

        var message = session.MainWindow.FindFirstDescendant(
            session.ConditionFactory.ByAutomationId("TaskSpaceRecoveryMessageText"));
        if (message == null ||
            message.Properties.IsOffscreen.ValueOrDefault ||
            message.Name.IndexOf(expectedProblemSourceId, StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException(
                $"Recovery message did not identify '{expectedProblemSourceId}'. Actual: '{message?.Name}'.");
        }

        var configAfterRecoveryLaunch = File.ReadAllText(configPath);
        var originalJson = JsonNode.Parse(originalConfig)?.AsObject()
                           ?? throw new InvalidOperationException("Original config is not a JSON object.");
        var recoveredJson = JsonNode.Parse(configAfterRecoveryLaunch)?.AsObject()
                            ?? throw new InvalidOperationException("Recovered config is not a JSON object.");
        foreach (var sectionName in new[]
                 {
                     "TaskSources",
                     "TaskSourceSyncProfiles",
                     "TaskSourceLegacyProjection",
                     "TaskSourceMutationJournal",
                     "TaskStorage",
                     "Git",
                     "ClientSettings"
                 })
        {
            originalJson.TryGetPropertyValue(sectionName, out var originalSection);
            recoveredJson.TryGetPropertyValue(sectionName, out var recoveredSection);
            if (!JsonNode.DeepEquals(originalSection, recoveredSection))
            {
                throw new InvalidOperationException(
                    $"Recovery launch modified task-space-owned config section '{sectionName}'.");
            }
        }
    }
}
