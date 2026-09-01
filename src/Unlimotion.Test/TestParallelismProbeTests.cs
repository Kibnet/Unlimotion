using System;
using System.Threading;
using System.Threading.Tasks;

namespace Unlimotion.Test;

[NotInParallel("ProbeSharedResource")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class TestParallelismProbeTests
{
    private static int _active;

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SharedConstraint_DoesNotOverlap(int instance)
    {
        using var trace = TestExecutionTrace.Resource("ProbeSharedResource");
        var active = Interlocked.Increment(ref _active);
        try
        {
            await Assert.That(active).IsEqualTo(1);
            await Task.Delay(30);
        }
        finally { Interlocked.Decrement(ref _active); }
    }
}

[NotInParallel("ProbeIndependentResource")]
public class IndependentTestParallelismProbeTests
{
    [Test]
    public async Task DifferentConstraint_RecordsActualExecution()
    {
        using var trace = TestExecutionTrace.Resource("ProbeIndependentResource");
        await Task.Delay(30);
        // The trace measures overlap; scheduler fairness is not an assertion.
    }
}
