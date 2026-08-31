using System;
using System.Diagnostics;

namespace Unlimotion.Test;

internal sealed class TestScenarioPhases(string name) : IDisposable
{
    private string _phase = "setup";
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public void Next(string phase)
    {
        TestExecutionTrace.Write("phase", name + "/" + _phase, "completed", _watch.Elapsed.TotalMilliseconds);
        _phase = phase;
        _watch.Restart();
    }

    public void Dispose() => TestExecutionTrace.Write("phase", name + "/" + _phase, "completed", _watch.Elapsed.TotalMilliseconds);
}
