using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Unlimotion.Desktop.Services;

public sealed class TaskDeepLinkActivationBroker : ITaskDeepLinkActivationSource, IDisposable
{
    private const string DefaultChannelName = "Unlimotion.TaskDeepLinkActivation";
    private static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(2);

    private readonly object gate = new();
    private readonly Queue<TaskDeepLink> pending = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Thread ownershipThread;
    private readonly string pipeName;
    private EventHandler<TaskDeepLinkActivationEventArgs>? activationRequested;
    private Task? serverTask;
    private bool disposed;

    public TaskDeepLinkActivationBroker(string? channelName = null)
    {
        var resolvedChannelName = string.IsNullOrWhiteSpace(channelName) ? DefaultChannelName : channelName;
        pipeName = resolvedChannelName;
        using var ownershipReady = new ManualResetEventSlim();
        var isOwner = false;
        Exception? ownershipError = null;
        ownershipThread = new Thread(() =>
        {
            try
            {
                HoldOwnership(GetOwnershipName(resolvedChannelName, OperatingSystem.IsWindows()), ownershipReady, value => isOwner = value);
            }
            catch (Exception exception)
            {
                ownershipError = exception;
                ownershipReady.Set();
            }
        })
        {
            IsBackground = true,
            Name = $"{resolvedChannelName}.Ownership"
        };
        ownershipThread.Start();
        ownershipReady.Wait();
        if (ownershipError is not null)
        {
            ownershipThread.Join();
            cancellation.Dispose();
            throw new InvalidOperationException("Unable to initialize task deep-link activation ownership.", ownershipError);
        }

        IsOwner = isOwner;
        if (IsOwner)
        {
            serverTask = Task.Run(() => ListenAsync(cancellation.Token));
        }
    }

    public bool IsOwner { get; }

    internal static string GetOwnershipName(string channelName, bool useWindowsNamespace) =>
        useWindowsNamespace ? $@"Local\{channelName}" : channelName;

    private void HoldOwnership(string mutexName, ManualResetEventSlim ready, Action<bool> reportOwnership)
    {
        using var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        reportOwnership(createdNew);
        ready.Set();
        if (!createdNew)
        {
            return;
        }

        cancellation.Token.WaitHandle.WaitOne();
        mutex.ReleaseMutex();
    }

    public event EventHandler<TaskDeepLinkActivationEventArgs>? ActivationRequested
    {
        add
        {
            lock (gate) activationRequested += value;
        }
        remove
        {
            lock (gate) activationRequested -= value;
        }
    }

    public void Enqueue(TaskDeepLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        EventHandler<TaskDeepLinkActivationEventArgs>? handler;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            handler = activationRequested;
            if (handler is null)
            {
                pending.Enqueue(link);
            }
        }

        handler?.Invoke(this, new TaskDeepLinkActivationEventArgs(link));
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

    public async Task<bool> TryForwardAsync(TaskDeepLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (IsOwner) return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ForwardTimeout);
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(link.Uri.AbsoluteUri).ConfigureAwait(false);
            var response = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            return string.Equals(response, "OK", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
                {
                    AutoFlush = true
                };
                var raw = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (TaskDeepLink.TryParse(raw, out var link))
                {
                    Enqueue(link!);
                    await writer.WriteLineAsync("OK").ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteLineAsync("INVALID").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                if (cancellationToken.IsCancellationRequested) return;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            activationRequested = null;
            pending.Clear();
        }

        cancellation.Cancel();
        var serverStopped = true;
        try
        {
            serverStopped = serverTask?.Wait(TimeSpan.FromSeconds(1)) ?? true;
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(static error => error is OperationCanceledException))
        {
        }

        ownershipThread.Join(TimeSpan.FromSeconds(1));
        if (serverStopped)
        {
            cancellation.Dispose();
        }
    }
}
