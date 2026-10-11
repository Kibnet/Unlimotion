using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public sealed class GitSyncSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "git-sync-settings-" + Guid.NewGuid().ToString("N"));
    public GitSyncSettingsTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Test]
    public async Task Binding_WaitsForCompleteProjectionAndRestoresAfterFailedSwitch()
    {
        var pathA = Path.Combine(_root, "A");
        var configPath = Path.Combine(_root, "binding.json");
        File.WriteAllText(configPath, "{}");
        var config = WritableJsonConfigurationFabric.Create(configPath, reloadOnChange: false);
        using var configLifetime = config as IDisposable;
        config.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Path)).Set(pathA);
        config.GetSection("Git").GetSection(nameof(GitSettings.BackupEnabled)).Set(true);
        var notifications = new NotificationManagerWrapperMock();
        var builder = new FailingBuilder(new TaskStorageBuilder(config, AppModelMapping.ConfigureMapping(), notifications, () => pathA));
        var manager = new TaskSourceManager(config, builder, notifications, () => pathA);
        var sourceA = manager.ActivateConfiguredSource();
        var settings = new SettingsViewModel(config);
        settings.UseDeferredTaskSpaceSettingsPersistence();
        using var status = new GitStatusViewModel(new ImmediateObserver());
        using var binding = new GitStatusSourceBinding(manager, status, action => action(), settings: settings);
        try
        {
            await GitStatusIndicatorTests.Until(() => status.LastKnown != null);
            await Assert.That(status.IsIndicatorVisible).IsTrue();
            var target = manager.AddConfiguredLocalSource("B", Path.Combine(_root, "B"));
            var sourceB = await manager.ActivateSourceByIdAsync(target.Id);
            await Assert.That(settings.GitBackupEnabled).IsTrue(); // Still A's field, runtime is already B.
            await Assert.That(status.IsIndicatorVisible).IsFalse();
            await Assert.That(status.LastKnown).IsNull();
            var partialWasHidden = false;
            ((INotifyPropertyChanged)settings).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.GitBackupEnabled))
                    partialWasHidden = settings.GitProjection == null && !status.IsIndicatorVisible;
            };
            settings.ReloadActiveTaskSpaceSettings();
            await Assert.That(partialWasHidden).IsTrue();
            await Assert.That(settings.GitProjection!.SourceVersion).IsSameReferenceAs(sourceB);
            await Assert.That(status.IsIndicatorVisible).IsFalse();
            settings.GitBackupEnabled = true;
            await GitStatusIndicatorTests.Until(() => status.LastKnown?.Request.SourceId == target.Id);
            var failed = manager.AddConfiguredLocalSource("Fails", Path.Combine(_root, "Fails"));
            settings.IsTaskSpaceSwitching = true;
            builder.FailId = failed.Id;
            await Assert.That(async () => await manager.ActivateSourceByIdAsync(failed.Id)).Throws<InvalidOperationException>();
            await Assert.That(manager.ActiveSource).IsSameReferenceAs(sourceB);
            settings.ReloadActiveTaskSpaceSettings();
            settings.IsTaskSpaceSwitching = false;
            // B's enabled edit was a deferred draft, so reload truthfully restores disabled config.
            await Assert.That(status.IsIndicatorVisible).IsFalse();
            settings.GitBackupEnabled = true;
            await GitStatusIndicatorTests.Until(() => status.LastKnown?.Request.SourceId == target.Id);
            await Assert.That(status.IsIndicatorVisible).IsTrue();
        }
        finally { foreach (var runtime in manager.Sources) (runtime.Storage as IDisposable)?.Dispose(); }
    }

    [Test]
    public async Task Settings_LateProjectionCompletionCannotPublishOldSourceIdentity()
    {
        var configPath = Path.Combine(_root, "projection.json");
        File.WriteAllText(configPath, "{}");
        var config = WritableJsonConfigurationFabric.Create(configPath, reloadOnChange: false);
        using var configLifetime = config as IDisposable;
        var settings = new SettingsViewModel(config);
        var projectionA = new GitSettingsProjection("A", _root, new object());
        var projectionB = new GitSettingsProjection("B", _root, new object());
        var current = projectionA;
        Func<GitSettingsProjection?> provider = () => current;
        settings.ConfigureGitProjection(provider);
        settings.GitBackupEnabled = true;
        ((INotifyPropertyChanged)settings).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.GitBackupEnabled)) current = projectionB;
        };
        config["Git:BackupEnabled"] = "false";
        settings.ReloadActiveTaskSpaceSettings();
        await Assert.That(settings.GitProjection).IsNull();
        settings.ReloadActiveTaskSpaceSettings();
        await Assert.That(settings.GitProjection).IsSameReferenceAs(projectionB);
        settings.ClearGitProjection(provider);
        await Assert.That(settings.GitProjection).IsNull();
    }

    private sealed class ImmediateObserver : IGitWorktreeObserver
    {
        public Task<GitWorktreeObservation> ObserveAsync(GitObservationRequest request, CancellationToken token) =>
            Task.FromResult(new GitWorktreeObservation(request, DateTimeOffset.UtcNow, GitObservationKind.Known, 1, false));
    }
    private sealed class FailingBuilder(ITaskStorageBuilder inner) : ITaskStorageBuilder
    {
        public string? FailId { get; set; }
        public TaskStorageBuildResult Build(TaskStorageBuildRequest request) => request.Descriptor.Id == FailId ?
            throw new InvalidOperationException("Fixture activation failure") : inner.Build(request);
    }
}
