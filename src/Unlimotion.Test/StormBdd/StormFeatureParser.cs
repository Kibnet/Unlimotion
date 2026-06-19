using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unlimotion.Test.StormBdd;

internal static class StormFeatureParser
{
    private static readonly string[] StepKeywords = ["Дано", "Когда", "Тогда", "И", "Но"];

    public static StormScenario ParseScenario(string relativePath, string scenarioId)
    {
        var path = PlatformShellProjectContracts.GetRepositoryPath(relativePath);
        var lines = File.ReadAllLines(path);
        var scenarioTagIndex = Array.FindIndex(
            lines,
            line => line.Contains($"@scenario:{scenarioId}", StringComparison.Ordinal));

        if (scenarioTagIndex < 0)
        {
            throw new InvalidOperationException($"Scenario tag @scenario:{scenarioId} was not found in {relativePath}.");
        }

        var tags = lines[scenarioTagIndex]
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        var ruleTitle = "";
        var scenarioTitle = "";
        var steps = new List<StormScenarioStep>();
        var inScenario = false;

        for (var index = scenarioTagIndex + 1; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("@", StringComparison.Ordinal) && inScenario)
            {
                break;
            }

            if (trimmed.StartsWith("Правило:", StringComparison.Ordinal))
            {
                if (inScenario)
                {
                    break;
                }

                ruleTitle = trimmed["Правило:".Length..].Trim();
                continue;
            }

            if (trimmed.StartsWith("Сценарий:", StringComparison.Ordinal))
            {
                if (inScenario)
                {
                    break;
                }

                scenarioTitle = trimmed["Сценарий:".Length..].Trim();
                inScenario = true;
                continue;
            }

            if (!inScenario)
            {
                continue;
            }

            if (!TryParseStep(trimmed, index + 1, out var step))
            {
                throw new InvalidOperationException(
                    $"Unsupported line inside scenario {scenarioId} at {relativePath}:{index + 1}: {trimmed}");
            }

            steps.Add(step);
        }

        if (string.IsNullOrWhiteSpace(scenarioTitle))
        {
            throw new InvalidOperationException($"Scenario title for {scenarioId} was not found in {relativePath}.");
        }

        if (steps.Count == 0)
        {
            throw new InvalidOperationException($"Scenario {scenarioId} has no executable steps in {relativePath}.");
        }

        return new StormScenario(scenarioId, scenarioTitle, ruleTitle, tags, steps);
    }

    private static bool TryParseStep(string line, int lineNumber, out StormScenarioStep step)
    {
        foreach (var keyword in StepKeywords)
        {
            if (!line.StartsWith(keyword + " ", StringComparison.Ordinal))
            {
                continue;
            }

            step = new StormScenarioStep(keyword, line[(keyword.Length + 1)..].Trim(), lineNumber);
            return true;
        }

        step = null!;
        return false;
    }
}
