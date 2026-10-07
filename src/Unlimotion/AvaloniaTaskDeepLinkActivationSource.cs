using System;
using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;

namespace Unlimotion;

internal sealed class AvaloniaTaskDeepLinkActivationSource : ITaskDeepLinkActivationSource, IDisposable
{
    private readonly object gate = new();
    private readonly Queue<TaskDeepLink> pending = new();
    private readonly IActivatableLifetime? lifetime;
    private EventHandler<TaskDeepLinkActivationEventArgs>? activationRequested;
    private bool disposed;

    public AvaloniaTaskDeepLinkActivationSource(IActivatableLifetime lifetime)
    {
        this.lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        lifetime.Activated += OnActivated;
    }

    internal AvaloniaTaskDeepLinkActivationSource()
    {
    }

    public event EventHandler<TaskDeepLinkActivationEventArgs>? ActivationRequested
    {
        add
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                activationRequested += value;
            }
        }
        remove
        {
            lock (gate)
            {
                activationRequested -= value;
            }
        }
    }

    public IReadOnlyList<TaskDeepLink> DrainPending()
    {
        lock (gate)
        {
            var result = pending.ToArray();
            pending.Clear();
            return result;
        }
    }

    private void OnActivated(object? sender, ActivatedEventArgs args) => ProcessActivation(args);

    internal void ProcessActivation(ActivatedEventArgs args)
    {
        if (args is not ProtocolActivatedEventArgs { Kind: ActivationKind.OpenUri } protocolActivation
            || !protocolActivation.Uri.IsAbsoluteUri
            || !TaskDeepLink.TryParse(protocolActivation.Uri.OriginalString, out var link))
        {
            return;
        }

        EventHandler<TaskDeepLinkActivationEventArgs>? handler;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            handler = activationRequested;
            if (handler is null)
            {
                pending.Enqueue(link!);
            }
        }

        handler?.Invoke(this, new TaskDeepLinkActivationEventArgs(link!));
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            activationRequested = null;
            pending.Clear();
        }

        if (lifetime is not null)
        {
            lifetime.Activated -= OnActivated;
        }
    }
}
