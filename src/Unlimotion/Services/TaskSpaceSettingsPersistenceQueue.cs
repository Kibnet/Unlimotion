using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public sealed class TaskSpaceSettingsPersistenceStateChangedEventArgs(
    bool hasPendingChanges,
    Exception? lastError) : EventArgs
{
    public bool HasPendingChanges { get; } = hasPendingChanges;
    public Exception? LastError { get; } = lastError;
}

public interface ITaskSpaceSettingsPersistenceQueue
{
    Exception? LastError { get; }
    bool HasPendingChanges { get; }
    event EventHandler<TaskSpaceSettingsPersistenceStateChangedEventArgs>? StateChanged;
    void Enqueue(TaskSpaceSettingsDraft draft);
    Task DrainAsync();
    Task RetryAsync();
}

public sealed class TaskSpaceSettingsPersistenceQueue(
    IActiveTaskSpaceConfiguration configuration,
    ITaskSpaceOperationRunner operationRunner,
    Func<TaskSpaceSettingsDraft, Task>? afterPersist = null)
    : ITaskSpaceSettingsPersistenceQueue
{
    private readonly object _sync = new();
    private readonly Dictionary<string, TaskSpaceSettingsDraft> _pending = new(StringComparer.Ordinal);
    private Task _worker = Task.CompletedTask;
    private bool _workerRunning;
    private Exception? _lastError;

    public event EventHandler<TaskSpaceSettingsPersistenceStateChangedEventArgs>? StateChanged;

    public Exception? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    public bool HasPendingChanges
    {
        get
        {
            lock (_sync)
            {
                return _workerRunning || _pending.Count != 0;
            }
        }
    }

    public void Enqueue(TaskSpaceSettingsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.SourceId);

        lock (_sync)
        {
            _pending[draft.SourceId] = ActiveTaskSpaceConfiguration.CloneDraft(draft);
            _lastError = null;
            if (!_workerRunning)
            {
                _workerRunning = true;
                _worker = Task.Run(ProcessAsync);
            }
        }

        NotifyStateChanged();
    }

    public async Task DrainAsync()
    {
        while (true)
        {
            Task worker;
            lock (_sync)
            {
                worker = _worker;
            }

            await worker.ConfigureAwait(false);

            lock (_sync)
            {
                if (_lastError != null)
                {
                    throw new InvalidOperationException(
                        "Pending task-space settings could not be persisted.",
                        _lastError);
                }

                if (_workerRunning || _pending.Count != 0)
                {
                    continue;
                }

                return;
            }
        }
    }

    public Task RetryAsync()
    {
        lock (_sync)
        {
            _lastError = null;
            if (_pending.Count != 0 && !_workerRunning)
            {
                _workerRunning = true;
                _worker = Task.Run(ProcessAsync);
            }
        }

        NotifyStateChanged();
        return DrainAsync();
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            KeyValuePair<string, TaskSpaceSettingsDraft> next;
            var finished = false;
            lock (_sync)
            {
                if (_pending.Count == 0)
                {
                    _workerRunning = false;
                    finished = true;
                    next = default;
                }
                else
                {
                    next = _pending.First();
                    _pending.Remove(next.Key);
                }
            }

            if (finished)
            {
                NotifyStateChanged();
                return;
            }

            try
            {
                await operationRunner.RunExclusiveAsync(
                    "PersistTaskSpaceSettings",
                    next.Key,
                    async context =>
                    {
                        configuration.PersistCore(context, next.Value);
                        if (afterPersist != null)
                        {
                            await afterPersist(
                                    ActiveTaskSpaceConfiguration.CloneDraft(next.Value))
                                .ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);
                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _pending[next.Key] = next.Value;
                    _lastError = ex;
                    _workerRunning = false;
                }

                NotifyStateChanged();
                return;
            }
        }
    }

    private void NotifyStateChanged()
    {
        TaskSpaceSettingsPersistenceStateChangedEventArgs state;
        lock (_sync)
        {
            state = new TaskSpaceSettingsPersistenceStateChangedEventArgs(
                _workerRunning || _pending.Count != 0,
                _lastError);
        }

        var handlers = StateChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (EventHandler<TaskSpaceSettingsPersistenceStateChangedEventArgs> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(this, state);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Task-space settings persistence state handler failed: {ex}");
            }
        }
    }
}
