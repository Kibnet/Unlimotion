using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test;

public class IndependentScenarioCasesTests
{
    [Test]
    public async Task AssertionFailure_RunsRemainingIndependentCases_AndFailsAggregate()
    {
        var calls = new List<string>();
        Exception? failure = null;
        try
        {
            await IndependentScenarioCases.RunAsync(
                ("first", async () =>
                {
                    try { calls.Add("first"); await Assert.That(false).IsTrue(); }
                    finally { calls.Add("cleanup"); }
                }),
                ("second", () => { calls.Add("second"); return Task.CompletedTask; }));
        }
        catch (AggregateException error) { failure = error; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(string.Join(",", calls)).IsEqualTo("first,cleanup,second");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationOrUnsafeCleanup_StopsRemainingCases(bool cancellation)
    {
        var sentinel = cancellation ? (Exception)new OperationCanceledException("cancel sentinel") : new InvalidOperationException("cleanup sentinel");
        var secondRan = false;
        Exception? observed = null;
        try
        {
            await IndependentScenarioCases.RunAsync(
                ("first", () => Task.FromException(sentinel)),
                ("second", () => { secondRan = true; return Task.CompletedTask; }));
        }
        catch (Exception error) { observed = error; }
        await Assert.That(ReferenceEquals(sentinel, observed)).IsTrue();
        await Assert.That(secondRan).IsFalse();
    }
}
