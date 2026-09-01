using System;
using System.IO;
using System.Threading.Tasks;

namespace Unlimotion.Test;

public class TestExecutionTraceTests
{
    [Test]
    public async Task FailingTraceSink_DoesNotInterruptCleanup_AndReportsOriginalFailure()
    {
        var writer = new DeferredTraceWriter();
        var failure = new IOException("trace disk unavailable");
        var cleaned = false;
        try { /* Scenario body. */ }
        finally
        {
            writer.TryWrite("test", () => throw failure);
            cleaned = true;
        }
        AggregateException? observed = null;
        try { writer.ThrowPending("test"); }
        catch (AggregateException error) { observed = error; }
        await Assert.That(cleaned).IsTrue();
        await Assert.That(observed).IsNotNull();
        await Assert.That(ReferenceEquals(observed!.InnerException, failure)).IsTrue();
        writer.ThrowPending("test"); // The hook consumes the error once.
    }
}
