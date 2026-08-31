using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using TUnit.Assertions.Exceptions;

namespace Unlimotion.Test;

internal static class IndependentScenarioCases
{
    // Only assertion failures are recoverable. A fixture/host/cleanup exception stops
    // the batch because continuing could use a contaminated process-wide UI state.
    public static async Task RunAsync(params (string Id, Func<Task> Execute)[] cases)
    {
        var failures = new List<Exception>();
        for (var index = 0; index < cases.Length; index++)
        {
            var item = cases[index];
            var watch = Stopwatch.StartNew();
            TestExecutionTrace.Write("subcase", item.Id, "started");
            try
            {
                await item.Execute();
                TestExecutionTrace.Write("subcase", item.Id, "passed", watch.Elapsed.TotalMilliseconds);
            }
            catch (AssertionException error)
            {
                failures.Add(error);
                TestExecutionTrace.Write("subcase", item.Id, "failed", watch.Elapsed.TotalMilliseconds,
                    new { error = error.ToString() });
            }
            catch (Exception error)
            {
                TestExecutionTrace.Write("subcase", item.Id, "aborted", watch.Elapsed.TotalMilliseconds,
                    new { error = error.ToString() });
                for (var remaining = index + 1; remaining < cases.Length; remaining++)
                    TestExecutionTrace.Write("subcase", cases[remaining].Id, "not-executed");
                if (failures.Count == 0) throw;
                failures.Add(error);
                throw new AggregateException("Independent scenario cases failed; unsafe continuation stopped.", failures);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("Independent scenario cases failed after running all safe cases.", failures);
    }
}
