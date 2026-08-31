using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using TUnit.Core;

namespace Unlimotion.Test;

internal static class TestExecutionTrace
{
    private static readonly object Sync = new();
    private static long _sequence;
    private static readonly DeferredTraceWriter Writer = new();
    private const string SessionKey = "session";

    public static void Write(string kind, string name, string outcome, double? durationMs = null, object? details = null)
    {
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_TEST_TRACE_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        Writer.TryWrite(TestContext.Current?.Id ?? SessionKey, () =>
        {
            lock (Sync)
            {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(new
            {
                sequence = Interlocked.Increment(ref _sequence), processId = Environment.ProcessId,
                utc = DateTimeOffset.UtcNow, timestamp = Stopwatch.GetTimestamp(), frequency = Stopwatch.Frequency,
                threadId = Environment.CurrentManagedThreadId, kind, name, outcome, durationMs, details
                , testExecutionId = TestContext.Current?.Id,
                testName = TestContext.Current?.Metadata.DisplayName
            });
            File.AppendAllText(Path.Combine(directory, $"diagnostics-{Environment.ProcessId}.jsonl"), json + Environment.NewLine);
            }
        });
    }

    public static void ThrowPendingErrors(string? testId = null) => Writer.ThrowPending(testId ?? SessionKey);

    public static IDisposable Phase(string name) => new PhaseScope(name);

    public static IDisposable Resource(string name)
    {
        var id = Guid.NewGuid().ToString("N");
        Write("resource", name, "entered", details: new { leaseId = id });
        return new ResourceScope(name, id);
    }

    private sealed class ResourceScope(string name, string id) : IDisposable
    {
        public void Dispose() => Write("resource", name, "left", details: new { leaseId = id });
    }

    private sealed class PhaseScope(string name) : IDisposable
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        public void Dispose() => Write("phase", name, "completed", _watch.Elapsed.TotalMilliseconds);
    }
}

// Telemetry must not interrupt a test's finally block. The test/session hook reports
// the original I/O error only after the resource cleanup has had a chance to run.
internal sealed class DeferredTraceWriter
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Exception>> _errors = new();

    public void TryWrite(string key, Action write)
    {
        try { write(); }
        catch (Exception error) { _errors.GetOrAdd(key, _ => new()).Enqueue(error); }
    }

    public void ThrowPending(string key)
    {
        if (!_errors.TryRemove(key, out var errors)) return;
        throw new AggregateException("Test telemetry failed after cleanup", errors);
    }
}
