using System;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Test;

public sealed class AgentExecutionDescriptionRendererTests
{
    [Test]
    public async Task Render_ReplacesOnlySingleMarkerBlockAndPreservesSurroundingText()
    {
        var first = CreateExecution("agent-a");
        var second = CreateExecution("agent-b");
        AgentExecutionDescriptionRenderer.TryRender("prefix", first, out var initial, out _);
        var withSuffix = initial + "suffix";

        var success = AgentExecutionDescriptionRenderer.TryRender(withSuffix, second, out var replaced, out var error);

        await Assert.That(success).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(replaced.StartsWith("prefix\n", StringComparison.Ordinal)).IsTrue();
        await Assert.That(replaced.EndsWith("suffix", StringComparison.Ordinal)).IsTrue();
        await Assert.That(replaced.Contains("Исполнитель: agent-b", StringComparison.Ordinal)).IsTrue();
        await Assert.That(replaced.Contains("agent-a", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_RejectsUnpairedAndRepeatedMarkersWithoutChangingInput()
    {
        var execution = CreateExecution("agent");
        var broken = "text " + AgentExecutionDescriptionRenderer.MarkerStart;
        var repeated = AgentExecutionDescriptionRenderer.MarkerStart + AgentExecutionDescriptionRenderer.MarkerEnd +
                       AgentExecutionDescriptionRenderer.MarkerStart + AgentExecutionDescriptionRenderer.MarkerEnd;

        var brokenSuccess = AgentExecutionDescriptionRenderer.TryRender(broken, execution, out var brokenResult, out _);
        var repeatedSuccess = AgentExecutionDescriptionRenderer.TryRender(repeated, execution, out var repeatedResult, out _);

        await Assert.That(brokenSuccess).IsFalse();
        await Assert.That(brokenResult).IsEqualTo(broken);
        await Assert.That(repeatedSuccess).IsFalse();
        await Assert.That(repeatedResult).IsEqualTo(repeated);
    }

    private static AgentExecutionRecord CreateExecution(string agent) => new()
    {
        AgentId = agent,
        LeaseId = Guid.NewGuid().ToString("D"),
        ClaimedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
