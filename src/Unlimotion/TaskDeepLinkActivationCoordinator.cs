using System;
using System.Collections.Generic;

namespace Unlimotion;

// The App owns readiness and UI dispatch; sources own OS/IPC subscriptions.
internal sealed class TaskDeepLinkActivationCoordinator(
    Func<bool> isReady, Action<TaskDeepLink> activate, Action<Action> dispatch) : IDisposable
{
    private readonly Queue<TaskDeepLink> pending = new();
    private ITaskDeepLinkActivationSource? source;
    private bool disposed;

    public void Attach(ITaskDeepLinkActivationSource activationSource)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ReferenceEquals(source, activationSource)) return;
        if (source is not null) source.ActivationRequested -= OnActivated;
        source = activationSource;
        source.ActivationRequested += OnActivated;
        foreach (var link in source.DrainPending()) Queue(link);
    }

    private void OnActivated(object? sender, TaskDeepLinkActivationEventArgs args) =>
        dispatch(() => Queue(args.Link));

    public void Queue(TaskDeepLink link)
    {
        if (disposed) return;
        pending.Enqueue(link);
        ProcessPending();
    }

    public void ProcessPending()
    {
        while (!disposed && isReady() && pending.TryDequeue(out var link)) activate(link);
    }

    public void Dispose()
    {
        disposed = true;
        if (source is not null) source.ActivationRequested -= OnActivated;
        source = null;
        pending.Clear();
    }
}
