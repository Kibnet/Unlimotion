using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Unlimotion.Test;

internal sealed class FaultMatrixTrace(string scenario, string[] writes) : IDisposable
{
    private readonly string _scenario = scenario;
    private readonly string[] _writes = writes;
    private readonly List<int> _executed = [];
    private readonly List<int> _passed = [];

    public Case StartCase(int index)
    {
        _executed.Add(index);
        return new Case(this, index);
    }

    public void Dispose() => TestExecutionTrace.Write("fault-matrix", scenario, "manifest", details: new
    {
        recordedFaultCount = writes.Length, recordedWrites = writes,
        executedFaultIndices = _executed, passedFaultIndices = _passed
    });

    internal sealed class Case(FaultMatrixTrace owner, int index) : IDisposable
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly TestScenarioPhases _phases = new($"{owner._scenario}/{index}/{owner._writes[index - 1]}");
        private bool _passed;
        public void Next(string phase) => _phases.Next(phase);
        public void Pass() { _passed = true; owner._passed.Add(index); }
        public void Dispose()
        {
            _phases.Dispose();
            TestExecutionTrace.Write("fault-case", owner._scenario, _passed ? "passed" : "failed",
                _watch.Elapsed.TotalMilliseconds, new { faultIndex = index, operation = owner._writes[index - 1] });
        }
    }
}
