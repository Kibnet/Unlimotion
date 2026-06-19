using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal sealed class StormScenarioRunner
{
    private readonly IReadOnlyDictionary<string, StormStepDefinition> definitionsByStep;

    public StormScenarioRunner(IEnumerable<StormStepDefinition> definitions)
    {
        var definitionList = definitions.ToArray();
        var duplicate = definitionList
            .GroupBy(definition => definition.MatchKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate != null)
        {
            throw new InvalidOperationException($"Duplicate STORM step definition text: {duplicate.Key}");
        }

        definitionsByStep = definitionList.ToDictionary(
            definition => definition.MatchKey,
            definition => definition,
            StringComparer.Ordinal);
    }

    public async Task<StormScenarioContext> ExecuteAsync(StormScenario scenario)
    {
        var context = new StormScenarioContext();
        foreach (var step in scenario.Steps)
        {
            var matchKey = CreateMatchKey(step.Keyword, step.Text);
            if (!definitionsByStep.TryGetValue(matchKey, out var definition))
            {
                throw new InvalidOperationException(
                    $"No STORM step definition for {scenario.Id} line {step.Line}: {step.Keyword} {step.Text}");
            }

            if (!definition.SupportsScenarios.Contains(scenario.Id))
            {
                throw new InvalidOperationException(
                    $"Step definition {definition.Id} does not support scenario {scenario.Id}.");
            }

            await definition.ExecuteAsync(context);
            context.ExecutedStepDefinitionIds.Add(definition.Id);
        }

        return context;
    }

    public static string CreateMatchKey(string keyword, string text)
    {
        return $"{Normalize(keyword)} {Normalize(text)}";
    }

    private static string Normalize(string value)
    {
        return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
