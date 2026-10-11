using System;
using System.ComponentModel;
using System.Threading;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

/// <summary>Captures source values at activation, without keeping an operation lease alive.</summary>
public sealed class GitStatusSourceBinding : IDisposable
{
    private readonly ITaskSourceManager _sources;
    private GitStatusViewModel _status;
    private readonly MainWindowViewModel? _owner;
    private readonly Action<Action> _dispatch;
    private readonly Func<(string? Remote, string? Branch)>? _captureConfiguration;
    private readonly SettingsViewModel? _settings;
    private readonly Func<GitSettingsProjection?> _projectionProvider;
    private FileStorage? _boundStorage;
    private string? _boundSource;
    private string? _boundPath;
    private object? _boundRuntime;
    private long _version;
    private bool _disposed;

    public GitStatusSourceBinding(ITaskSourceManager sources, GitStatusViewModel status, Action<Action> dispatch,
        Func<(string? Remote, string? Branch)>? captureConfiguration = null, SettingsViewModel? settings = null,
        MainWindowViewModel? owner = null)
    {
        _sources = sources;
        _status = status;
        _owner = owner;
        _dispatch = dispatch;
        _captureConfiguration = captureConfiguration;
        _settings = settings;
        _projectionProvider = CaptureProjection;
        if (_sources is ITaskSourceActivationObservation activation) activation.ActiveSourceChanged += SourceChanged;
        if (_owner != null) ((INotifyPropertyChanged)_owner).PropertyChanged += OwnerChanged;
        if (_settings != null)
        {
            ((INotifyPropertyChanged)_settings).PropertyChanged += SettingsChanged;
            _settings.ConfigureGitProjection(_projectionProvider);
        }
        SourceChanged(this, EventArgs.Empty);
    }

    private void OwnerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed || _owner == null || args.PropertyName is not (null or nameof(MainWindowViewModel.GitStatus))) return;
        if (ReferenceEquals(_status, _owner.GitStatus)) return;
        _status = _owner.GitStatus;
        _boundRuntime = null;
        SourceChanged(sender, EventArgs.Empty);
    }

    private GitSettingsProjection? CaptureProjection()
    {
        var runtime = _sources.ActiveSource;
        if (runtime == null) return null;
        var storage = runtime.Storage.TaskTreeManager.Storage as FileStorage;
        return new(runtime.Descriptor.Id, storage == null ? "" : GitObservationRequest.CanonicalPath(storage.Path), runtime);
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(SettingsViewModel.GitProjection) or
            nameof(SettingsViewModel.GitBackupEnabled) or nameof(SettingsViewModel.IsTaskSpaceSwitching) or
            nameof(SettingsViewModel.IsServerMode) or nameof(SettingsViewModel.GitRemoteName) or
            nameof(SettingsViewModel.GitBranch)) SourceChanged(sender, EventArgs.Empty);
    }

    private void SourceChanged(object? sender, EventArgs args)
    {
        var runtime = _sources.ActiveSource;
        var fileStorage = runtime?.Storage.TaskTreeManager.Storage as FileStorage;
        var id = runtime?.Descriptor.Id ?? "";
        var path = fileStorage?.Path ?? "";
        var applicable = fileStorage != null && runtime?.Descriptor.Kind == TaskSourceKind.File;
        var version = Interlocked.Increment(ref _version);
        _dispatch(() =>
        {
            if (_disposed || version != _version) return;
            if (!ReferenceEquals(runtime, _sources.ActiveSource)) return;
            if (_boundSource != id || !GitObservationRequest.PathComparer.Equals(_boundPath, path) ||
                !ReferenceEquals(_boundStorage, fileStorage) || !ReferenceEquals(_boundRuntime, runtime))
            {
                _status.SetEligibility(false, false);
                _boundSource = id;
                _boundPath = path;
                _boundStorage = fileStorage;
                _boundRuntime = runtime;
                _status.SetSource(id, path, applicable, fileStorage as IStorageMutationObservation, sourceVersion: runtime);
            }
            var ready = _settings == null || (!_settings.IsTaskSpaceSwitching &&
                _settings.GitProjection == CaptureProjection());
            if (ready)
            {
                var configuration = _settings == null ? _captureConfiguration?.Invoke() ?? default :
                    (_settings.GitRemoteName, _settings.GitBranch);
                _status.UpdateTargetContext(configuration.Item1, configuration.Item2);
            }
            _status.SetEligibility(_settings?.GitBackupEnabled ?? true, ready);
        });
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Increment(ref _version);
        if (_sources is ITaskSourceActivationObservation activation) activation.ActiveSourceChanged -= SourceChanged;
        if (_owner != null) ((INotifyPropertyChanged)_owner).PropertyChanged -= OwnerChanged;
        if (_settings != null)
        {
            ((INotifyPropertyChanged)_settings).PropertyChanged -= SettingsChanged;
            _settings.ClearGitProjection(_projectionProvider);
        }
    }
}
