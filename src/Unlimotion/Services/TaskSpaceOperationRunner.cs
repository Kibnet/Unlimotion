using System;
using System.Threading;
using System.Threading.Tasks;

namespace Unlimotion.Services;

public sealed class TaskSpaceOperationContext
{
    private readonly object _owner;

    internal TaskSpaceOperationContext(object owner, string operationName, string? sourceId)
    {
        _owner = owner;
        OperationId = Guid.NewGuid();
        OperationName = operationName;
        SourceId = sourceId;
    }

    public Guid OperationId { get; }
    public string OperationName { get; }
    public string? SourceId { get; }

    internal bool IsOwnedBy(object owner) => ReferenceEquals(_owner, owner);
}

public interface ITaskSpaceOperationRunner
{
    bool IsBusy { get; }

    Task RunExclusiveAsync(
        string operationName,
        string? sourceId,
        Func<TaskSpaceOperationContext, Task> operation,
        CancellationToken cancellationToken = default);

    Task<T> RunExclusiveAsync<T>(
        string operationName,
        string? sourceId,
        Func<TaskSpaceOperationContext, Task<T>> operation,
        CancellationToken cancellationToken = default);

    void Validate(TaskSpaceOperationContext context);
}

public interface ITaskSpaceBackupOperationScope
{
    IDisposable BeginTaskSpaceOperation(TaskSpaceOperationContext context);
}

public sealed class TaskSpaceOperationRunner : ITaskSpaceOperationRunner, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<TaskSpaceOperationContext?> _currentContext = new();
    private readonly object _owner = new();
    private int _busy;
    private bool _disposed;

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public Task RunExclusiveAsync(
        string operationName,
        string? sourceId,
        Func<TaskSpaceOperationContext, Task> operation,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync<object?>(
            operationName,
            sourceId,
            async context =>
            {
                await operation(context).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    public async Task<T> RunExclusiveAsync<T>(
        string operationName,
        string? sourceId,
        Func<TaskSpaceOperationContext, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(operation);

        if (_currentContext.Value != null)
        {
            throw new InvalidOperationException(
                "A task-space operation already owns the exclusive lease. Pass its context to core methods instead of acquiring another lease.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var context = new TaskSpaceOperationContext(_owner, operationName, sourceId);
        _currentContext.Value = context;
        Volatile.Write(ref _busy, 1);
        try
        {
            return await operation(context).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
            _currentContext.Value = null;
            _gate.Release();
        }
    }

    public void Validate(TaskSpaceOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.IsOwnedBy(_owner) || !ReferenceEquals(_currentContext.Value, context))
        {
            throw new InvalidOperationException("The task-space operation context is not active for this runner.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
