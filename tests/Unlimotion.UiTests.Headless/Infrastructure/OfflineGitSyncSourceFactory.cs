using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Unlimotion.Domain;
using Unlimotion.Services;
using Unlimotion.ViewModel;

namespace Unlimotion.UiTests.Headless.Infrastructure;

/// <summary>Real source activation/selector with a local carrier for the server UI; no server transport.</summary>
internal sealed class OfflineGitSyncSourceFactory : ITaskStorageFactory
{
    public ITaskSourceManager SourceManager { get; }
    public OfflineGitSyncSourceFactory(IConfiguration configuration)
    {
        var pathA = configuration.Get<TaskStorageSettings>("TaskStorage").Path;
        var fixtureRoot = Path.GetDirectoryName(pathA)!;
        var serverPath = Path.Combine(fixtureRoot, "offline-server");
        var builder = new TaskStorageBuilder(configuration, AppModelMapping.ConfigureMapping(), defaultStoragePathProvider: () => pathA);
        SourceManager = new TaskSourceManager(configuration, new OfflineBuilder(builder, serverPath), defaultStoragePathProvider: () => pathA);
        var sourceA = SourceManager.ConfiguredSources.Single(source => source.DisplayName == "Space A");
        var draftA = SourceManager.GetSourceSettings(sourceA.Id);
        draftA.Git.BackupEnabled = true;
        SourceManager.PersistSourceSettings(draftA);
        var pathC = Path.Combine(fixtureRoot, "space-C");
        Seed(pathC, "Space C task");
        var sourceC = SourceManager.AddConfiguredLocalSource("Space C", pathC);
        var draftC = SourceManager.GetSourceSettings(sourceC.Id);
        draftC.Git.BackupEnabled = true;
        SourceManager.PersistSourceSettings(draftC);
        Seed(serverPath, "Offline server task");
        var server = SourceManager.AddConfiguredLocalSource("Offline server", Path.Combine(fixtureRoot, "server-descriptor"));
        var draftServer = SourceManager.GetSourceSettings(server.Id);
        draftServer.Storage.IsServerMode = true;
        draftServer.Storage.URL = "https://offline-server-fixture.invalid";
        draftServer.Storage.Login = "fixture";
        draftServer.Git.BackupEnabled = true;
        SourceManager.PersistSourceSettings(draftServer);
    }
    public ITaskStorage CreateConfiguredStorage() => SourceManager.ActivateConfiguredSource().Storage;
    public ITaskStorage CreateFileStorage(string? path) => throw new NotSupportedException();
    public ITaskStorage CreateDetachedFileStorage(string? path) => throw new NotSupportedException();
    public ITaskStorage CreateServerStorage(string? url) => throw new NotSupportedException();
    public void SwitchStorage(bool isServerMode, IConfiguration configuration) => throw new NotSupportedException();

    private static void Seed(string path, string title)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "git-sync-fixture"), JsonSerializer.Serialize(new TaskItem
        { Id = "git-sync-fixture", Title = title, CreatedDateTime = DateTime.UtcNow, Version = 1 }));
    }
    private sealed class OfflineBuilder(ITaskStorageBuilder inner, string serverPath) : ITaskStorageBuilder
    {
        public TaskStorageBuildResult Build(TaskStorageBuildRequest request) => request.Descriptor.Kind != TaskSourceKind.Server ?
            inner.Build(request) : inner.Build(new TaskStorageBuildRequest
            {
                Descriptor = new TaskSourceDescriptor { Id = request.Descriptor.Id, Kind = TaskSourceKind.File, Path = serverPath },
                TaskContext = request.TaskContext,
                EnableWatcher = request.EnableWatcher
            });
    }
}
