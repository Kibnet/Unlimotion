using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public interface ITaskSpaceSettingsPersistenceQueue
{
    Exception? LastError { get; }
    void Enqueue(TaskSpaceSettingsDraft draft);
    Task DrainAsync();
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

    private async Task ProcessAsync()
    {
        while (true)
        {
            KeyValuePair<string, TaskSpaceSettingsDraft> next;
            lock (_sync)
            {
                if (_pending.Count == 0)
                {
                    _workerRunning = false;
                    return;
                }

                next = _pending.First();
                _pending.Remove(next.Key);
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
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _pending[next.Key] = next.Value;
                    _lastError = ex;
                    _workerRunning = false;
                }

                return;
            }
        }
    }
}
