using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Unlimotion.Services;

public sealed class TaskSpaceRecoveryException(
    Exception activationError,
    Exception restorationError)
    : AggregateException(
        "The target task space failed and the previous task space could not be restored. Restart is required.",
        activationError,
        restorationError)
{
    public Exception ActivationError { get; } = activationError;
    public Exception RestorationError { get; } = restorationError;
}

public sealed class TaskSpaceCoordinator(
    ITaskSourceManager sourceManager,
    ITaskSpaceOperationRunner operationRunner,
    ITaskSpaceSettingsPersistenceQueue settingsQueue,
    Func<TaskSourceRuntime, Task> bindInitializedRuntime,
    Func<Task> clearTaskSurface,
    Func<Task> pauseScheduler,
    Func<TaskSourceRuntime?, Task> restoreScheduler)
{
    public async Task<TaskSourceRuntime> SwitchAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (string.Equals(sourceManager.ActiveSource?.Descriptor.Id, sourceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Task space '{sourceId}' is already active.");
        }

        return await ActivateAsync(sourceId, cancellationToken).ConfigureAwait(false);
    }

    public Task<TaskSourceRuntime> AddLocalAsync(
        string displayName,
        string? path = null,
        CancellationToken cancellationToken = default) =>
        ActivateAsync(
            operationName: "AddTaskSpace",
            sourceId: null,
            context => sourceManager.PrepareAddLocalActivationCoreAsync(
                context,
                displayName,
                path),
            cancellationToken);

    public Task<TaskSourceRuntime> ReconnectActiveAsync(CancellationToken cancellationToken = default)
    {
        var sourceId = sourceManager.ActiveSource?.Descriptor.Id
            ?? throw new InvalidOperationException("There is no active task space to reconnect.");
        return ActivateAsync(
            "ReconnectTaskSpace",
            sourceId,
            context => sourceManager.PrepareActivationCoreAsync(context, sourceId),
            cancellationToken);
    }

    public async Task RenameAsync(
        string sourceId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        await settingsQueue.DrainAsync().ConfigureAwait(false);
        await operationRunner.RunExclusiveAsync(
                "RenameTaskSpace",
                sourceId,
                _ =>
                {
                    sourceManager.RenameConfiguredSource(sourceId, displayName);
                    return Task.CompletedTask;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (sourceManager.ConfiguredSources.Count <= 1)
        {
            throw new InvalidOperationException("At least one task space must remain configured.");
        }

        if (string.Equals(
                sourceManager.ActiveSource?.Descriptor.Id,
                sourceId,
                StringComparison.Ordinal))
        {
            var fallback = sourceManager.ConfiguredSources.First(source =>
                !string.Equals(source.Id, sourceId, StringComparison.Ordinal));
            await SwitchAsync(fallback.Id, cancellationToken).ConfigureAwait(false);
        }

        await settingsQueue.DrainAsync().ConfigureAwait(false);
        await operationRunner.RunExclusiveAsync(
                "RemoveTaskSpace",
                sourceId,
                _ =>
                {
                    sourceManager.RemoveConfiguredSource(sourceId);
                    return Task.CompletedTask;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TaskSourceRuntime> ActivateAsync(
        string sourceId,
        CancellationToken cancellationToken)
    {
        if (sourceManager.ConfiguredSources.All(source =>
                !string.Equals(source.Id, sourceId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Task space '{sourceId}' is not configured.");
        }

        return await ActivateAsync(
                "SwitchTaskSpace",
                sourceId,
                context => sourceManager.PrepareActivationCoreAsync(context, sourceId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TaskSourceRuntime> ActivateAsync(
        string operationName,
        string? sourceId,
        Func<TaskSpaceOperationContext, Task<TaskSourceActivation>> prepareActivation,
        CancellationToken cancellationToken)
    {
        await pauseScheduler().ConfigureAwait(false);
        try
        {
            await settingsQueue.DrainAsync().ConfigureAwait(false);
            // Persist callbacks may reconfigure and resume Quartz for the old active
            // profile. Re-pause after the drain so no fresh job can enter while the
            // switch waits for the exclusive operation lease.
            await pauseScheduler().ConfigureAwait(false);
            return await operationRunner.RunExclusiveAsync(
                operationName,
                sourceId,
                context => SwitchCoreAsync(context, prepareActivation),
                cancellationToken).ConfigureAwait(false);
        }
        catch (TaskSpaceRecoveryException)
        {
            throw;
        }
        catch (Exception activationError)
        {
            var activeSource = sourceManager.ActiveSource;
            if (activeSource == null)
            {
                try
                {
                    await clearTaskSurface().ConfigureAwait(false);
                }
                catch (Exception clearError)
                {
                    throw new TaskSpaceRecoveryException(activationError, clearError);
                }

                throw;
            }

            try
            {
                await restoreScheduler(activeSource).ConfigureAwait(false);
            }
            catch (Exception restorationError)
            {
                try
                {
                    await clearTaskSurface().ConfigureAwait(false);
                }
                catch (Exception clearError)
                {
                    restorationError = new AggregateException(restorationError, clearError);
                }

                throw new TaskSpaceRecoveryException(activationError, restorationError);
            }

            throw;
        }
    }

    private async Task<TaskSourceRuntime> SwitchCoreAsync(
        TaskSpaceOperationContext context,
        Func<TaskSpaceOperationContext, Task<TaskSourceActivation>> prepareActivation)
    {
        operationRunner.Validate(context);
        TaskSourceActivation? activation = null;
        try
        {
            activation = await prepareActivation(context).ConfigureAwait(false);
            await bindInitializedRuntime(activation.Candidate).ConfigureAwait(false);
            await sourceManager.PublishActivationCoreAsync(context, activation).ConfigureAwait(false);
            await restoreScheduler(activation.Candidate).ConfigureAwait(false);
            return activation.Candidate;
        }
        catch (Exception activationError)
        {
            if (activation == null)
            {
                throw;
            }

            try
            {
                await sourceManager.AbortActivationCoreAsync(context, activation).ConfigureAwait(false);
                if (activation.Previous != null)
                {
                    await bindInitializedRuntime(activation.Previous).ConfigureAwait(false);
                    await restoreScheduler(activation.Previous).ConfigureAwait(false);
                }
                else
                {
                    await clearTaskSurface().ConfigureAwait(false);
                }
            }
            catch (Exception restorationError)
            {
                try
                {
                    await clearTaskSurface().ConfigureAwait(false);
                }
                catch (Exception clearError)
                {
                    restorationError = new AggregateException(restorationError, clearError);
                }

                throw new TaskSpaceRecoveryException(activationError, restorationError);
            }

            throw;
        }
    }
}
