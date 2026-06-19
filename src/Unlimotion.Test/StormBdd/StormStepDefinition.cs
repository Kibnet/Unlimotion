using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal sealed record StormScenario(
    string Id,
    string Title,
    string RuleTitle,
    IReadOnlyList<string> Tags,
    IReadOnlyList<StormScenarioStep> Steps);

internal sealed record StormScenarioStep(string Keyword, string Text, int Line);

internal sealed record StormStepDefinition(
    string Id,
    string Keyword,
    string Text,
    IReadOnlySet<string> SupportsScenarios,
    Func<StormScenarioContext, Task> ExecuteAsync)
{
    public string MatchKey => StormScenarioRunner.CreateMatchKey(Keyword, Text);
}

internal sealed class StormScenarioContext
{
    public bool DesktopReleaseSupportedShellConfirmed { get; set; }

    public bool NonDesktopSharedUiModelRequired { get; set; }

    public bool PlatformProjectContractsChecked { get; set; }

    public bool RuntimeReleaseSupportClaimed { get; set; }

    public HashSet<string> ExecutedStepDefinitionIds { get; } = new(StringComparer.Ordinal);
}
