using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.TaskTree;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

/// <summary>Ephemeral source-bound samples and operation diagnostics. All state is delivered on the UI dispatcher.</summary>
public sealed class GitStatusViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IGitWorktreeObserver? _observer;
    private readonly Action<Action> _dispatch;
    private readonly Func<DateTimeOffset> _now;
    private readonly IBackupOperationDiagnostics? _diagnostics;
    private readonly Timer _timer;
    private readonly HashSet<Guid> _operations = new();
    private CancellationTokenSource _sourceCancellation = new();
    private IStorageMutationObservation? _storage;
    private GitObservationRequest? _request;
    private GitWorktreeObservation? _sample;
    private string? _readError;
    private string? _operationError;
    private string? _previousOperationError;
    private long _generation;
    private object? _sourceVersion;
    private long _readEpoch;
    private bool _enabled;
    private bool _projectionReady;
    private bool _pending;
    private bool _disposed;
    private bool _visible;
    private DateTimeOffset _nextRead;
    private long _readingGeneration = -1;
    private bool _queued;
    public event PropertyChangedEventHandler? PropertyChanged;

    public GitStatusViewModel(IGitWorktreeObserver? observer = null, Action<Action>? dispatch = null,
        IBackupOperationDiagnostics? diagnostics = null, Func<DateTimeOffset>? now = null)
    {
        _observer = observer;
        _dispatch = dispatch ?? (action => action());
        _diagnostics = diagnostics;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        if (_diagnostics != null) _diagnostics.OperationDiagnostic += OnDiagnostic;
        L10n.Current.CultureChanged += HandleCultureChanged;
        _timer = new Timer(_ => _dispatch(Tick), null, Timeout.Infinite, Timeout.Infinite);
    }

    public GitWorktreeObservation? LastKnown => _sample;
    public bool IsIndicatorVisible => !_disposed && _enabled && _projectionReady && _request?.Applicable == true;
    public string SettingsInformation => _request?.Applicable == false ? L10n.Get("GitIndicatorServer") :
        !_projectionReady ? L10n.Get("GitIndicatorSettingsLoading") :
        !_enabled ? L10n.Get("GitIndicatorDisabled") : Details;
    public bool IsBusy => _operations.Count != 0;
    public bool HasDirty => _sample?.DirtyFileCount > 0;
    public bool IsStale => _pending || _readError != null ||
        (_sample != null && _now() - _sample.ObservedAt > TimeSpan.FromSeconds(30));
    public GitIndicatorState State => _request?.Applicable == false ? GitIndicatorState.NotApplicable :
        _sample?.HasConflicts == true || _operationError != null ? GitIndicatorState.Attention :
        IsBusy ? GitIndicatorState.Syncing : HasDirty ? GitIndicatorState.Dirty :
        _sample == null || IsStale ? GitIndicatorState.Unknown : GitIndicatorState.Idle;
    public string Glyph => State switch
    {
        GitIndicatorState.NotApplicable => "—", GitIndicatorState.Attention => "!",
        GitIndicatorState.Syncing => "↻", GitIndicatorState.Dirty => "✎",
        GitIndicatorState.Idle => "✓", _ => "?"
    };
    public string DirtyBadge => HasDirty ? _sample!.DirtyFileCount!.Value.ToString() : "";
    public string DivergenceGlyph => _sample?.Ahead > 0 || _sample?.Behind > 0 ? "⇅" : "";
    public string StaleGlyph => IsStale ? "·" : "";
    public string Summary => L10n.Get("GitIndicator" + State);
    public string ToolTip => Summary + (HasDirty ? "\n" + L10n.Format("GitIndicatorFiles", DirtyBadge) : "") +
        (IsStale ? "\n" + L10n.Get("GitIndicatorStale") : "");
    public string Details => Summary + (IsStale ? "\n" + L10n.Get("GitIndicatorStale") : "") + "\n" +
        L10n.Format("GitIndicatorFiles", _sample?.DirtyFileCount?.ToString() ?? "—") + "\n" +
        L10n.Format("GitIndicatorCommits", _sample?.Ahead?.ToString() ?? "—", _sample?.Behind?.ToString() ?? "—") + "\n" +
        L10n.Format("GitIndicatorConflicts", _sample?.HasConflicts is bool conflicts ? L10n.Get(conflicts ? "GitIndicatorYes" : "GitIndicatorNo") : "—") + "\n" +
        L10n.Format("GitIndicatorObserved", _sample?.ObservedAt.ToLocalTime().ToString("HH:mm:ss") ?? "—") + "\n" +
        L10n.Get("GitIndicatorLocalRefs") +
        (_readError != null ? "\n" + L10n.Get("GitIndicatorReadError") : "") +
        (_operationError != null ? "\n" + L10n.Get("GitIndicatorOperationError") : "") +
        (_previousOperationError != null ? "\n" + L10n.Get("GitIndicatorPreviousError") : "");
    public string SettingsLabel => L10n.Get("GitIndicatorSettings");

    public void SetSource(string sourceId, string path, bool applicable,
        IStorageMutationObservation? storage = null, string? remoteName = null, string? branchName = null,
        object? sourceVersion = null)
    {
        if (_disposed) return;
        if (_storage != null) _storage.StorageBytesMayHaveChanged -= HandleStorageChanged;
        _sourceCancellation.Cancel();
        _sourceCancellation.Dispose();
        _sourceCancellation = new();
        ++_generation;
        _sourceVersion = sourceVersion;
        _request = new(sourceId, applicable ? GitObservationRequest.CanonicalPath(path) : "", ++_readEpoch,
            applicable, remoteName, branchName);
        _sample = null;
        _readError = _operationError = _previousOperationError = null;
        _operations.Clear();
        _pending = false;
        _queued = false;
        _storage = storage;
        if (_storage != null) _storage.StorageBytesMayHaveChanged += HandleStorageChanged;
        UpdateTimer();
        Changed();
        RequestRefresh();
    }

    public void SetEligibility(bool enabled, bool projectionReady)
    {
        if (_disposed || (_enabled == enabled && _projectionReady == projectionReady)) return;
        _enabled = enabled;
        _projectionReady = projectionReady;
        InvalidateRead();
        _sample = null;
        _readError = null;
        UpdateTimer();
        Changed();
        RequestRefresh();
    }

    public void UpdateTargetContext(string? remoteName, string? branchName)
    {
        if (_disposed || _request == null ||
            (_request.RemoteName == remoteName && _request.BranchName == branchName)) return;
        InvalidateRead();
        _request = _request with { RemoteName = remoteName, BranchName = branchName };
        if (_sample != null) _sample = _sample with { Ahead = null, Behind = null };
        Changed();
        RequestRefresh();
    }

    private void InvalidateRead()
    {
        _sourceCancellation.Cancel();
        _sourceCancellation.Dispose();
        _sourceCancellation = new();
        ++_readEpoch;
        if (_request != null) _request = _request with { Generation = _readEpoch };
        _readingGeneration = -1;
        _pending = _queued = false;
    }

    public void SetVisible(bool visible)
    {
        if (_disposed) return;
        _visible = visible;
        UpdateTimer();
        if (visible) RequestRefresh();
    }

    private void UpdateTimer()
    {
        var active = _visible && IsIndicatorVisible && _observer != null;
        _timer.Change(active ? 1000 : Timeout.Infinite, active ? 1000 : Timeout.Infinite);
    }

    public void RequestRefresh()
    {
        if (_disposed || !IsIndicatorVisible || _observer == null) return;
        _pending = true;
        _nextRead = _now().AddSeconds(15);
        Changed();
        if (_readingGeneration == _readEpoch) { _queued = true; return; }
        _readingGeneration = _readEpoch;
        _ = ReadAsync(_request!, _sourceCancellation.Token);
    }

    private async Task ReadAsync(GitObservationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            var observation = await _observer!.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
            _dispatch(() =>
            {
                if (!Matches(observation.Request)) return;
                if (observation.Kind == GitObservationKind.Known) { _sample = observation; _readError = null; }
                else _readError = observation.ErrorCode ?? "Unknown";
                FinishRead(request);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _dispatch(() => { if (Matches(request)) { _readError = "ReadFailed"; FinishRead(request); } });
        }
    }

    private void FinishRead(GitObservationRequest request)
    {
        if (!Matches(request)) return;
        _pending = false;
        _readingGeneration = -1;
        _nextRead = _now().AddSeconds(15);
        Changed();
        if (_queued) { _queued = false; RequestRefresh(); }
    }

    private bool Matches(GitObservationRequest request) => !_disposed && _request != null &&
        IsIndicatorVisible && request.Generation == _readEpoch && request.SourceId == _request.SourceId &&
        GitObservationRequest.PathComparer.Equals(request.RepositoryPath, _request.RepositoryPath);

    private void OnDiagnostic(BackupOperationDiagnostic diagnostic)
    {
        var generation = Interlocked.Read(ref _generation);
        _dispatch(() =>
        {
            // A tagged event belongs to its captured runtime, even when UI source binding is queued.
            if (diagnostic.SourceVersion != null || generation == _generation) ApplyDiagnostic(diagnostic);
        });
    }
    public void ApplyDiagnostic(BackupOperationDiagnostic diagnostic)
    {
        if (_disposed || _request == null || diagnostic.SourceId != _request.SourceId ||
            !GitObservationRequest.PathComparer.Equals(diagnostic.RepositoryPath, _request.RepositoryPath) ||
            (diagnostic.SourceVersion != null && !ReferenceEquals(diagnostic.SourceVersion, _sourceVersion))) return;
        if (diagnostic.Started)
        {
            if (_operations.Count == 0) { _previousOperationError = _operationError ?? _previousOperationError; _operationError = null; }
            _operations.Add(diagnostic.OperationId);
        }
        else
        {
            if (!_operations.Remove(diagnostic.OperationId)) return;
            if (diagnostic.Result == BackupOperationResult.Failed) _operationError = diagnostic.ErrorCode ?? "Failed";
            if (diagnostic.Result is BackupOperationResult.Cancelled or BackupOperationResult.SkippedUnknown) _readError = "UnknownOutcome";
            RequestRefresh();
        }
        Changed();
    }

    private void Tick()
    {
        if (_disposed || !_visible || !IsIndicatorVisible || _observer == null) return;
        Changed();
        if (_now() >= _nextRead) RequestRefresh();
    }
    private void HandleStorageChanged(object? sender, EventArgs args) => _dispatch(() => { if (ReferenceEquals(sender, _storage)) RequestRefresh(); });
    private void HandleCultureChanged(object? sender, EventArgs args) => _dispatch(Changed);
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sourceCancellation.Cancel();
        _sourceCancellation.Dispose();
        _timer.Dispose();
        if (_storage != null) _storage.StorageBytesMayHaveChanged -= HandleStorageChanged;
        if (_diagnostics != null) _diagnostics.OperationDiagnostic -= OnDiagnostic;
        L10n.Current.CultureChanged -= HandleCultureChanged;
    }
}
