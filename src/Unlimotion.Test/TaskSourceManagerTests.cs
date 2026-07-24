using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.Domain;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public sealed class TaskSourceManagerTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        $"TaskSourceManagerTests_{Guid.NewGuid():N}");
    private readonly List<IDisposable> _disposables = [];

    public TaskSourceManagerTests()
    {
        Directory.CreateDirectory(_rootPath);
    }

    [Test]
    public async Task LoadOrCreate_ProjectsLegacyLocalStorageToDefaultSource()
    {
        var configuration = CreateConfiguration();
        var legacyPath = Path.Combine(_rootPath, "legacy-tasks");
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Path)).Set(legacyPath);
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.IsServerMode)).Set(false);

        var settings = TaskSourceSettingsAdapter.LoadOrCreate(configuration, "fallback-tasks");

        await Assert.That(settings.ActiveSourceId).IsEqualTo(TaskSourceDescriptor.DefaultSourceId);
        await Assert.That(settings.Sources.Count).IsEqualTo(1);
        await Assert.That(settings.Sources[0].Kind).IsEqualTo(TaskSourceKind.File);
        await Assert.That(settings.Sources[0].Path).IsEqualTo(legacyPath);
        await Assert.That(TaskSourceSettingsAdapter.LoadOrCreate(configuration, "fallback-tasks").Sources[0].Path)
            .IsEqualTo(legacyPath);
    }

    [Test]
    public async Task LoadOrCreate_ProjectsLegacyServerCredentialsToDefaultSource()
    {
        var configuration = CreateConfiguration();
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.IsServerMode)).Set(true);
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.URL)).Set("https://tasks.example");
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Login)).Set("legacy-login");
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Password)).Set("legacy-password");
        configuration.Set("ClientSettings", new ClientSettings
        {
            AccessToken = "legacy-access",
            RefreshToken = "legacy-refresh",
            UserId = "legacy-user",
            Login = "legacy-client-login",
            ExpireTime = DateTimeOffset.UtcNow.AddHours(1)
        });

        var settings = TaskSourceSettingsAdapter.LoadOrCreate(configuration, "fallback-tasks");
        var source = settings.Sources.Single();
        var serverSettings = settings.ServerSettings.Single();

        await Assert.That(source.Kind).IsEqualTo(TaskSourceKind.Server);
        await Assert.That(source.Url).IsEqualTo("https://tasks.example");
        await Assert.That(serverSettings.SourceId).IsEqualTo(TaskSourceDescriptor.DefaultSourceId);
        await Assert.That(serverSettings.Login).IsEqualTo("legacy-login");
        await Assert.That(serverSettings.Password).IsEqualTo("legacy-password");
        await Assert.That(serverSettings.AccessToken).IsEqualTo("legacy-access");
        await Assert.That(serverSettings.RefreshToken).IsEqualTo("legacy-refresh");
        await Assert.That(serverSettings.UserId).IsEqualTo("legacy-user");
    }

    [Test]
    public async Task EnsureServerSettings_DoesNotCopyLegacyTokensToNonDefaultSource()
    {
        var configuration = CreateConfiguration();
        configuration.Set("ClientSettings", new ClientSettings
        {
            AccessToken = "default-access",
            RefreshToken = "default-refresh",
            UserId = "default-user",
            Login = "default-login",
            ExpireTime = DateTimeOffset.UtcNow.AddHours(1)
        });
        var settings = new TaskSourcesSettings();
        var legacyStorage = new TaskStorageSettings
        {
            Login = "work-login",
            Password = "work-password"
        };

        var serverSettings = TaskSourceSettingsAdapter.EnsureServerSettings(
            settings,
            "work",
            legacyStorage,
            configuration);

        await Assert.That(serverSettings.SourceId).IsEqualTo("work");
        await Assert.That(serverSettings.Login).IsEqualTo("work-login");
        await Assert.That(serverSettings.Password).IsEqualTo("work-password");
        await Assert.That(serverSettings.AccessToken).IsEmpty();
        await Assert.That(serverSettings.RefreshToken).IsEmpty();
        await Assert.That(serverSettings.UserId).IsEmpty();
    }

    [Test]
    public async Task SourceManager_ActivatesPersistedActiveSource()
    {
        var configuration = CreateConfiguration();
        var defaultPath = Path.Combine(_rootPath, "default");
        var workPath = Path.Combine(_rootPath, "work");
        TaskSourceSettingsAdapter.Save(configuration, new TaskSourcesSettings
        {
            ActiveSourceId = "work",
            Sources =
            [
                new TaskSourceDescriptor
                {
                    Id = TaskSourceDescriptor.DefaultSourceId,
                    Kind = TaskSourceKind.File,
                    Path = defaultPath
                },
                new TaskSourceDescriptor
                {
                    Id = "work",
                    Kind = TaskSourceKind.File,
                    Path = workPath
                }
            ]
        });
        var builder = new RecordingTaskStorageBuilder();
        var manager = new TaskSourceManager(configuration, builder, defaultStoragePathProvider: () => defaultPath);

        var activeSource = manager.ActivateConfiguredSource();
        var persisted = TaskSourceSettingsAdapter.LoadOrCreate(configuration, defaultPath);

        await Assert.That(activeSource.Descriptor.Id).IsEqualTo("work");
        await Assert.That(activeSource.Descriptor.Path).IsEqualTo(workPath);
        await Assert.That(builder.Builds.Single().Descriptor.Id).IsEqualTo("work");
        await Assert.That(builder.Builds.Single().Descriptor.Path).IsEqualTo(workPath);
        await Assert.That(persisted.ActiveSourceId).IsEqualTo("work");
    }

    [Test]
    public async Task SourceManager_SwitchStorageUpdatesActiveNonDefaultSource()
    {
        var configuration = CreateConfiguration();
        var defaultPath = Path.Combine(_rootPath, "default");
        var initialWorkPath = Path.Combine(_rootPath, "work-a");
        var selectedWorkPath = Path.Combine(_rootPath, "work-b");
        TaskSourceSettingsAdapter.Save(configuration, new TaskSourcesSettings
        {
            ActiveSourceId = "work",
            Sources =
            [
                new TaskSourceDescriptor
                {
                    Id = TaskSourceDescriptor.DefaultSourceId,
                    Kind = TaskSourceKind.File,
                    Path = defaultPath
                },
                new TaskSourceDescriptor
                {
                    Id = "work",
                    DisplayName = "Work",
                    Kind = TaskSourceKind.File,
                    Path = initialWorkPath
                }
            ]
        });
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Path)).Set(selectedWorkPath);
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.IsServerMode)).Set(false);
        var builder = new RecordingTaskStorageBuilder();
        var manager = new TaskSourceManager(configuration, builder, defaultStoragePathProvider: () => defaultPath);
        manager.ActivateConfiguredSource();

        await manager.SwitchStorageAsync(isServerMode: false, configuration);
        var persisted = TaskSourceSettingsAdapter.LoadOrCreate(configuration, defaultPath);
        var activeSource = persisted.Sources.Single(source => source.Id == "work");

        await Assert.That(manager.ActiveSource?.Descriptor.Id).IsEqualTo("work");
        await Assert.That(manager.ActiveSource?.Descriptor.Path).IsEqualTo(selectedWorkPath);
        await Assert.That(activeSource.Path).IsEqualTo(selectedWorkPath);
        await Assert.That(persisted.ActiveSourceId).IsEqualTo("work");
        await Assert.That(builder.Builds.Last().Descriptor.Id).IsEqualTo("work");
    }

    [Test]
    public async Task SourceManager_ReplacesSameSourceIdAndDisconnectsPreviousStorage()
    {
        var configuration = CreateConfiguration();
        var builder = new RecordingTaskStorageBuilder();
        var manager = new TaskSourceManager(
            configuration,
            builder,
            defaultStoragePathProvider: () => Path.Combine(_rootPath, "default"));
        var firstPath = Path.Combine(_rootPath, "default-a");
        var secondPath = Path.Combine(_rootPath, "default-b");

        var first = manager.ActivateSource(new TaskSourceDescriptor
        {
            Id = TaskSourceDescriptor.DefaultSourceId,
            Kind = TaskSourceKind.File,
            Path = firstPath
        });
        var second = manager.ActivateSource(new TaskSourceDescriptor
        {
            Id = TaskSourceDescriptor.DefaultSourceId,
            Kind = TaskSourceKind.File,
            Path = secondPath
        });

        await Assert.That(first.Storage).IsNotSameReferenceAs(second.Storage);
        await Assert.That(builder.Builds[0].Storage.DisconnectCalls).IsEqualTo(1);
        await Assert.That(manager.Sources.Count).IsEqualTo(1);
        await Assert.That(manager.ActiveSource).IsSameReferenceAs(second);
        await Assert.That(manager.ActiveSource!.Descriptor.Path).IsEqualTo(secondPath);
    }

    [Test]
    public async Task SourceManager_BuildFailureKeepsPreviousActiveSourceAlive()
    {
        var configuration = CreateConfiguration();
        var defaultPath = Path.Combine(_rootPath, "default");
        var builder = new RecordingTaskStorageBuilder();
        var manager = new TaskSourceManager(
            configuration,
            builder,
            defaultStoragePathProvider: () => defaultPath);

        var first = manager.ActivateSource(new TaskSourceDescriptor
        {
            Id = TaskSourceDescriptor.DefaultSourceId,
            Kind = TaskSourceKind.File,
            Path = defaultPath
        });
        builder.ThrowForSourceId = "work";

        await Assert.That(async () => await manager.ActivateSourceAsync(new TaskSourceDescriptor
            {
                Id = "work",
                Kind = TaskSourceKind.File,
                Path = Path.Combine(_rootPath, "work")
            }))
            .Throws<InvalidOperationException>();
        var persisted = TaskSourceSettingsAdapter.LoadOrCreate(configuration, defaultPath);

        await Assert.That(manager.ActiveSource).IsSameReferenceAs(first);
        await Assert.That(first.Storage.TaskTreeManager.Storage is RecordingStorage { DisconnectCalls: 0 }).IsTrue();
        await Assert.That(manager.Sources.Select(source => source.Descriptor.Id)).DoesNotContain("work");
        await Assert.That(persisted.ActiveSourceId).IsEqualTo(TaskSourceDescriptor.DefaultSourceId);
        await Assert.That(persisted.Sources.Select(source => source.Id)).DoesNotContain("work");
    }

    [Test]
    public async Task SourceManager_CreatesTwoFileSourcesWithoutSharingStorageOrContext()
    {
        var configuration = CreateConfiguration();
        var notificationManager = new NotificationManagerWrapperMock();
        var mapper = AppModelMapping.ConfigureMapping();
        var builder = new TaskStorageBuilder(configuration, mapper, notificationManager, () => Path.Combine(_rootPath, "default"));
        var manager = new TaskSourceManager(configuration, builder, notificationManager, () => Path.Combine(_rootPath, "default"));

        var first = manager.ActivateSource(new TaskSourceDescriptor
        {
            Id = "local-a",
            Kind = TaskSourceKind.File,
            Path = Path.Combine(_rootPath, "local-a")
        });
        var second = manager.ActivateSource(new TaskSourceDescriptor
        {
            Id = "local-b",
            Kind = TaskSourceKind.File,
            Path = Path.Combine(_rootPath, "local-b")
        });

        try
        {
            await Assert.That(first.Storage).IsNotSameReferenceAs(second.Storage);
            await Assert.That(first.TaskContext).IsNotSameReferenceAs(second.TaskContext);
            await Assert.That(first.TaskContext.SourceId).IsEqualTo("local-a");
            await Assert.That(second.TaskContext.SourceId).IsEqualTo("local-b");
            await Assert.That(first.TaskContext.NotificationManager).IsSameReferenceAs(notificationManager);
            await Assert.That(manager.ActiveSource).IsSameReferenceAs(second);
            await Assert.That(manager.Sources.Count).IsEqualTo(1);
        }
        finally
        {
            (first.Storage as IDisposable)?.Dispose();
            (second.Storage as IDisposable)?.Dispose();
        }
    }

    [Test]
    public async Task TaskStorageFactory_CreateDetachedFileStorageDoesNotSwitchActiveSource()
    {
        var configuration = CreateConfiguration();
        var defaultPath = Path.Combine(_rootPath, "default");
        var detachedPath = Path.Combine(_rootPath, "detached");
        var notificationManager = new NotificationManagerWrapperMock();
        var mapper = AppModelMapping.ConfigureMapping();
        var factory = new TaskStorageFactory(configuration, mapper, notificationManager, () => defaultPath);
        var activeStorage = factory.CreateConfiguredStorage();
        var activeSource = factory.SourceManager.ActiveSource;

        var detachedStorage = factory.CreateDetachedFileStorage(detachedPath);

        try
        {
            await Assert.That(factory.SourceManager.ActiveSource).IsSameReferenceAs(activeSource);
            await Assert.That(factory.SourceManager.ActiveStorage).IsSameReferenceAs(activeStorage);
            await Assert.That((detachedStorage.TaskTreeManager.Storage as FileStorage)?.Path).IsEqualTo(detachedPath);
        }
        finally
        {
            (activeStorage as IDisposable)?.Dispose();
            (detachedStorage as IDisposable)?.Dispose();
        }
    }

    [Test]
    public async Task TaskItemViewModel_ReceivesSourceContextAndKeepsOwningStorage()
    {
        var firstStorage = new RecordingStorage();
        var secondStorage = new RecordingStorage();
        var firstRepository = new UnifiedTaskStorage(
            new TaskTreeManager(firstStorage),
            new TaskItemViewModelContext
            {
                SourceId = "source-a",
                NotificationManager = new NotificationManagerWrapperMock()
            });
        var secondRepository = new UnifiedTaskStorage(
            new TaskTreeManager(secondStorage),
            new TaskItemViewModelContext { SourceId = "source-b" });

        var firstTask = new TaskItemViewModel(
            new TaskItem { Id = "task-a", Title = "Task A" },
            firstRepository,
            () => false,
            context: new TaskItemViewModelContext { SourceId = "source-a" });
        _ = new TaskItemViewModel(
            new TaskItem { Id = "task-b", Title = "Task B" },
            secondRepository,
            () => false,
            context: new TaskItemViewModelContext { SourceId = "source-b" });

        await firstTask.SaveItemCommand.Execute().ToTask();

        await Assert.That(firstTask.SourceId).IsEqualTo("source-a");
        await Assert.That(firstStorage.SavedTaskIds.Contains("task-a")).IsTrue();
        await Assert.That(secondStorage.SavedTaskIds).IsEmpty();
    }

    [Test]
    public async Task TaskStorage_RejectsRelationsBetweenDifferentTaskSpaces()
    {
        var firstRepository = new UnifiedTaskStorage(
            new TaskTreeManager(new RecordingStorage()),
            new TaskItemViewModelContext { SourceId = "source-a" });
        var secondRepository = new UnifiedTaskStorage(
            new TaskTreeManager(new RecordingStorage()),
            new TaskItemViewModelContext { SourceId = "source-b" });
        var firstTask = new TaskItemViewModel(
            new TaskItem { Id = "task-a", Title = "Task A" },
            firstRepository,
            () => false,
            new TaskItemViewModelContext { SourceId = "source-a" });
        var secondTask = new TaskItemViewModel(
            new TaskItem { Id = "task-b", Title = "Task B" },
            secondRepository,
            () => false,
            new TaskItemViewModelContext { SourceId = "source-b" });

        await Assert.That(async () => await firstRepository.Block(firstTask, secondTask))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TaskStorage_RejectsSameSourceOperandsWhenExecutingStorageOwnsAnotherSpace()
    {
        var executingRepository = new UnifiedTaskStorage(
            new TaskTreeManager(new RecordingStorage()),
            new TaskItemViewModelContext { SourceId = "source-b" });
        var owningRepository = new UnifiedTaskStorage(
            new TaskTreeManager(new RecordingStorage()),
            new TaskItemViewModelContext { SourceId = "source-a" });
        var firstTask = new TaskItemViewModel(
            new TaskItem { Id = "task-a", Title = "Task A" },
            owningRepository,
            () => false,
            new TaskItemViewModelContext { SourceId = "source-a" });
        var secondTask = new TaskItemViewModel(
            new TaskItem { Id = "task-b", Title = "Task B" },
            owningRepository,
            () => false,
            new TaskItemViewModelContext { SourceId = "source-a" });

        await Assert.That(async () => await executingRepository.Block(firstTask, secondTask))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TaskStorage_RejectsRelationOperandWithoutSourceIdentity()
    {
        var repository = new UnifiedTaskStorage(
            new TaskTreeManager(new RecordingStorage()),
            new TaskItemViewModelContext { SourceId = "source-a" });
        var identifiedTask = new TaskItemViewModel(
            new TaskItem { Id = "identified", Title = "Identified" },
            repository,
            () => false,
            new TaskItemViewModelContext { SourceId = "source-a" });
        var unidentifiedTask = new TaskItemViewModel(
            new TaskItem { Id = "unidentified", Title = "Unidentified" },
            repository,
            () => false,
            new TaskItemViewModelContext { SourceId = string.Empty });

        await Assert.That(async () => await repository.Block(identifiedTask, unidentifiedTask))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ServerSettings_PersistDistinctTokensPerSource()
    {
        var configuration = CreateConfiguration();
        var settings = new TaskSourcesSettings
        {
            ActiveSourceId = TaskSourceDescriptor.DefaultSourceId,
            Sources =
            [
                new TaskSourceDescriptor
                {
                    Id = TaskSourceDescriptor.DefaultSourceId,
                    Kind = TaskSourceKind.Server,
                    Url = "https://default.example"
                },
                new TaskSourceDescriptor
                {
                    Id = "work",
                    Kind = TaskSourceKind.Server,
                    Url = "https://work.example"
                }
            ]
        };

        TaskSourceSettingsAdapter.PersistServerSettings(configuration, settings, new TaskSourceServerSettings
        {
            SourceId = TaskSourceDescriptor.DefaultSourceId,
            Login = "default-login",
            AccessToken = "default-access",
            RefreshToken = "default-refresh"
        });
        TaskSourceSettingsAdapter.PersistServerSettings(configuration, settings, new TaskSourceServerSettings
        {
            SourceId = "work",
            Login = "work-login",
            AccessToken = "work-access",
            RefreshToken = "work-refresh"
        });

        var persisted = TaskSourceSettingsAdapter.LoadOrCreate(configuration, "fallback-tasks");
        var defaultServer = persisted.ServerSettings.Single(server => server.SourceId == TaskSourceDescriptor.DefaultSourceId);
        var workServer = persisted.ServerSettings.Single(server => server.SourceId == "work");
        var legacyClientSettings = configuration.Get<ClientSettings>("ClientSettings");

        await Assert.That(defaultServer.AccessToken).IsEqualTo("default-access");
        await Assert.That(workServer.AccessToken).IsEqualTo("work-access");
        await Assert.That(defaultServer.RefreshToken).IsEqualTo("default-refresh");
        await Assert.That(workServer.RefreshToken).IsEqualTo("work-refresh");
        await Assert.That(legacyClientSettings?.AccessToken).IsEqualTo("default-access");
        await Assert.That(legacyClientSettings?.Login).IsEqualTo("default-login");
    }

    [Test]
    public async Task ServerStorage_DisconnectDoesNotClearSourceTokens()
    {
        var configuration = CreateConfiguration();
        var serverSettings = new TaskSourceServerSettings
        {
            SourceId = "work",
            AccessToken = "access-token",
            RefreshToken = "refresh-token"
        };
        var persistCalls = 0;
        var storage = new ServerStorage(
            "https://tasks.example",
            configuration,
            serverSettings,
            _ => persistCalls++);

        await storage.Disconnect();

        await Assert.That(serverSettings.AccessToken).IsEqualTo("access-token");
        await Assert.That(serverSettings.RefreshToken).IsEqualTo("refresh-token");
        await Assert.That(persistCalls).IsEqualTo(0);
    }

    [Test]
    public async Task SyncProfiles_ProjectOnlyTheActiveSpaceIntoLegacyGitSettings()
    {
        var configuration = CreateConfiguration();
        var settings = new TaskSourcesSettings
        {
            ActiveSourceId = "personal",
            Sources =
            [
                new TaskSourceDescriptor { Id = "personal", Kind = TaskSourceKind.File, Path = Path.Combine(_rootPath, "personal") },
                new TaskSourceDescriptor { Id = "work", Kind = TaskSourceKind.File, Path = Path.Combine(_rootPath, "work") }
            ],
            SyncSettings =
            [
                new TaskSourceSyncSettings { SourceId = "personal", Git = new GitSettings { RemoteUrl = "https://example.test/personal.git", Branch = "personal" } },
                new TaskSourceSyncSettings { SourceId = "work", Git = new GitSettings { RemoteUrl = "https://example.test/work.git", Branch = "work" } }
            ]
        };

        TaskSourceSettingsAdapter.Save(configuration, settings);
        TaskSourceSettingsAdapter.SyncLegacy(configuration, settings, settings.Sources[0]);
        var personalLegacy = configuration.Get<GitSettings>("Git");

        settings.ActiveSourceId = "work";
        TaskSourceSettingsAdapter.SyncLegacy(configuration, settings, settings.Sources[1]);
        var workLegacy = configuration.Get<GitSettings>("Git");
        var reloaded = TaskSourceSettingsAdapter.LoadOrCreate(configuration, Path.Combine(_rootPath, "fallback"));

        await Assert.That(personalLegacy?.RemoteUrl).IsEqualTo("https://example.test/personal.git");
        await Assert.That(workLegacy?.RemoteUrl).IsEqualTo("https://example.test/work.git");
        await Assert.That(reloaded.SyncSettings.Single(sync => sync.SourceId == "personal").Git.Branch)
            .IsEqualTo("personal");
        await Assert.That(reloaded.SyncSettings.Single(sync => sync.SourceId == "work").Git.Branch)
            .IsEqualTo("work");
    }

    [Test]
    public async Task SourceManager_AddRenameActivateAndRemoveSpace_PreservesOtherProfiles()
    {
        var configuration = CreateConfiguration();
        var defaultPath = Path.Combine(_rootPath, "default");
        var manager = new TaskSourceManager(
            configuration,
            new RecordingTaskStorageBuilder(),
            defaultStoragePathProvider: () => defaultPath);
        manager.ActivateConfiguredSource();

        var created = manager.AddConfiguredLocalSource("Work", Path.Combine(_rootPath, "work"));
        manager.RenameConfiguredSource(created.Id, "Work tasks");
        await manager.ActivateSourceByIdAsync(created.Id);
        var activeGit = configuration.Get<GitSettings>("Git");

        await manager.ActivateSourceByIdAsync(TaskSourceDescriptor.DefaultSourceId);
        manager.RemoveConfiguredSource(created.Id);
        var persisted = TaskSourceSettingsAdapter.LoadOrCreate(configuration, defaultPath);

        await Assert.That(activeGit).IsNotNull();
        await Assert.That(manager.ActiveSource?.Descriptor.Id).IsEqualTo(TaskSourceDescriptor.DefaultSourceId);
        await Assert.That(persisted.Sources.Select(source => source.Id)).DoesNotContain(created.Id);
        await Assert.That(persisted.SyncSettings.Select(sync => sync.SourceId)).DoesNotContain(created.Id);
        await Assert.That(persisted.Sources.Single().Id).IsEqualTo(TaskSourceDescriptor.DefaultSourceId);
    }

    [Test]
    public async Task SourceManager_AddLocalSpaceRejectsNormalizedAliasOfExistingDirectory()
    {
        var configuration = CreateConfiguration();
        var defaultPath = Path.Combine(_rootPath, "default");
        Directory.CreateDirectory(defaultPath);
        var manager = new TaskSourceManager(
            configuration,
            new RecordingTaskStorageBuilder(),
            defaultStoragePathProvider: () => defaultPath);
        var normalizedAlias = Path.Combine(defaultPath, "..", Path.GetFileName(defaultPath));

        await Assert.That(() => manager.AddConfiguredLocalSource("Alias", normalizedAlias))
            .Throws<InvalidOperationException>();
        await Assert.That(manager.ConfiguredSources.Select(source => source.Id))
            .IsEquivalentTo([TaskSourceDescriptor.DefaultSourceId]);
    }

    [Test]
    public async Task SourceManager_LocalPathIdentityUsesPlatformCaseSemantics()
    {
        var configuration = CreateConfiguration();
        var defaultPath = Path.Combine(_rootPath, "case-sensitive");
        Directory.CreateDirectory(defaultPath);
        var manager = new TaskSourceManager(
            configuration,
            new RecordingTaskStorageBuilder(),
            defaultStoragePathProvider: () => defaultPath);
        var alternateCasePath = Path.Combine(_rootPath, "CASE-SENSITIVE");

        if (OperatingSystem.IsWindows())
        {
            await Assert.That(() => manager.AddConfiguredLocalSource("Alias", alternateCasePath))
                .Throws<InvalidOperationException>();
            return;
        }

        var created = manager.AddConfiguredLocalSource("Distinct", alternateCasePath);

        await Assert.That(manager.ConfiguredSources.Select(source => source.Id)).Contains(created.Id);
        await Assert.That(Directory.Exists(alternateCasePath)).IsTrue();
    }

    [Test]
    public async Task SourceManager_ServerOwnershipRejectsSameNormalizedUrlAndLogin()
    {
        var configuration = CreateConfiguration();
        var manager = new TaskSourceManager(
            configuration,
            new RecordingTaskStorageBuilder(),
            defaultStoragePathProvider: () => Path.Combine(_rootPath, "default"));
        var first = manager.AddConfiguredLocalSource("Server one", Path.Combine(_rootPath, "server-one"));
        var second = manager.AddConfiguredLocalSource("Server two", Path.Combine(_rootPath, "server-two"));

        manager.PersistSourceSettings(CreateServerDraft(
            first.Id,
            "https://tasks.example/",
            "ExampleUser"));

        await Assert.That(() => manager.PersistSourceSettings(CreateServerDraft(
                second.Id,
                " HTTPS://TASKS.EXAMPLE ",
                " exampleuser ")))
            .Throws<InvalidOperationException>();

        manager.PersistSourceSettings(CreateServerDraft(
            second.Id,
            "https://tasks.example",
            "another-user"));

        await Assert.That(manager.GetSourceSettings(second.Id).Storage.IsServerMode).IsTrue();
        await Assert.That(manager.GetSourceSettings(second.Id).Storage.Login).IsEqualTo("another-user");
    }

    [Test]
    public async Task SourceManager_PersistLocalSettingsRejectsEmptyPathBeforeMutation()
    {
        var configuration = CreateConfiguration();
        var defaultPath = Path.Combine(_rootPath, "default");
        var manager = new TaskSourceManager(
            configuration,
            new RecordingTaskStorageBuilder(),
            defaultStoragePathProvider: () => defaultPath);
        var workPath = Path.Combine(_rootPath, "work");
        var work = manager.AddConfiguredLocalSource("Work", workPath);

        await Assert.That(() => manager.PersistSourceSettings(new TaskSpaceSettingsDraft
            {
                SourceId = work.Id,
                Storage = new TaskStorageSettings
                {
                    IsServerMode = false,
                    Path = " "
                },
                Git = new GitSettings()
            }))
            .Throws<InvalidOperationException>();

        await Assert.That(manager.GetSourceSettings(work.Id).Storage.Path).IsEqualTo(workPath);
        await Assert.That(manager.ConfiguredSources.Single(source => source.Id == work.Id).Path)
            .IsEqualTo(workPath);
    }

    [Test]
    [Arguments("https://tasks.example/api/", "https://TASKS.example:443/api")]
    [Arguments("https://tasks.example./api", "https://tasks.example/api")]
    public async Task SourceManager_ServerOwnershipRejectsCanonicalUrlAliases(
        string firstUrl,
        string secondUrl)
    {
        var configuration = CreateConfiguration();
        var manager = new TaskSourceManager(
            configuration,
            new RecordingTaskStorageBuilder(),
            defaultStoragePathProvider: () => Path.Combine(_rootPath, "default"));
        var first = manager.AddConfiguredLocalSource("Server one", Path.Combine(_rootPath, "server-one"));
        var second = manager.AddConfiguredLocalSource("Server two", Path.Combine(_rootPath, "server-two"));

        manager.PersistSourceSettings(CreateServerDraft(
            first.Id,
            firstUrl,
            "example-user"));

        await Assert.That(() => manager.PersistSourceSettings(CreateServerDraft(
                second.Id,
                secondUrl,
                " example-user ")))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SourceManager_ServerOwnershipPreservesCaseSensitiveEndpointPaths()
    {
        var configuration = CreateConfiguration();
        var manager = new TaskSourceManager(
            configuration,
            new RecordingTaskStorageBuilder(),
            defaultStoragePathProvider: () => Path.Combine(_rootPath, "default"));
        var first = manager.AddConfiguredLocalSource("Server one", Path.Combine(_rootPath, "server-one"));
        var second = manager.AddConfiguredLocalSource("Server two", Path.Combine(_rootPath, "server-two"));

        manager.PersistSourceSettings(CreateServerDraft(
            first.Id,
            "https://tasks.example/API",
            "example-user"));
        manager.PersistSourceSettings(CreateServerDraft(
            second.Id,
            "https://TASKS.EXAMPLE/api",
            "example-user"));

        await Assert.That(manager.GetSourceSettings(first.Id).Storage.URL)
            .IsEqualTo("https://tasks.example/API");
        await Assert.That(manager.GetSourceSettings(second.Id).Storage.URL)
            .IsEqualTo("https://tasks.example/api");
    }

    [Test]
    public async Task SourceManager_InvalidCatalogFailsBeforeConfigurationPersistence()
    {
        var path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        var configuration = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        var defaultPath = Path.Combine(_rootPath, "default");
        var settings = TaskSourceSettingsAdapter.LoadOrCreate(configuration, defaultPath);
        settings.Sources.Add(new TaskSourceDescriptor
        {
            Id = "duplicate-folder",
            DisplayName = "Duplicate folder",
            Kind = TaskSourceKind.File,
            Path = defaultPath,
            IsEnabled = true
        });
        TaskSourceSettingsAdapter.EnsureSyncSettings(settings, "duplicate-folder");
        TaskSourceSettingsAdapter.Save(configuration, settings);
        var persistedBeforeValidation = File.ReadAllText(path);

        await Assert.That(() => new TaskSourceManager(
                configuration,
                new RecordingTaskStorageBuilder(),
                defaultStoragePathProvider: () => defaultPath))
            .Throws<TaskSpaceCatalogException>();

        await Assert.That(File.ReadAllText(path)).IsEqualTo(persistedBeforeValidation);
    }

    [Test]
    public async Task SourceManager_InvalidConfiguredFilePathFailsAsCatalogErrorBeforePersistence()
    {
        var path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        var configuration = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        var settings = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));
        settings.Sources[0].Path = $"invalid{'\0'}path";
        TaskSourceSettingsAdapter.Save(configuration, settings);
        var persistedBeforeValidation = File.ReadAllText(path);

        await Assert.That(() => new TaskSourceManager(
                configuration,
                new RecordingTaskStorageBuilder(),
                defaultStoragePathProvider: () => Path.Combine(_rootPath, "default")))
            .Throws<TaskSpaceCatalogException>();

        await Assert.That(File.ReadAllText(path)).IsEqualTo(persistedBeforeValidation);
    }

    [Test]
    public async Task SourceManager_UnsupportedConfiguredSourceKindFailsAsCatalogErrorBeforePersistence()
    {
        var path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        var configuration = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        var settings = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));
        settings.Sources[0].Kind = (TaskSourceKind)int.MaxValue;
        TaskSourceSettingsAdapter.Save(configuration, settings);
        var persistedBeforeValidation = File.ReadAllText(path);

        await Assert.That(() => new TaskSourceManager(
                configuration,
                new RecordingTaskStorageBuilder(),
                defaultStoragePathProvider: () => Path.Combine(_rootPath, "default")))
            .Throws<TaskSpaceCatalogException>();

        await Assert.That(File.ReadAllText(path)).IsEqualTo(persistedBeforeValidation);
    }

    [Test]
    public async Task SourceManager_InvalidPreparedJournalSnapshotFailsBeforeConfigurationPersistence()
    {
        var path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        var configuration = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        var defaultPath = Path.Combine(_rootPath, "default");
        var current = TaskSourceSettingsAdapter.LoadOrCreate(configuration, defaultPath);
        var corruptBefore = JsonSerializer.Deserialize<TaskSourcesSettings>(
                                JsonSerializer.Serialize(current))
                            ?? throw new InvalidOperationException("Could not clone task-space settings.");
        corruptBefore.Sources.Add(new TaskSourceDescriptor
        {
            Id = "journal-duplicate-folder",
            DisplayName = "Journal duplicate folder",
            Kind = TaskSourceKind.File,
            Path = defaultPath,
            IsEnabled = true
        });
        TaskSourceSettingsAdapter.EnsureSyncSettings(corruptBefore, "journal-duplicate-folder");

        var journal = configuration.GetSection("TaskSourceMutationJournal");
        journal.GetSection("MutationId").Set("invalid-snapshot");
        journal.GetSection("Operation").Set("Add");
        journal.GetSection("BeforeSnapshot").Set(JsonSerializer.Serialize(corruptBefore));
        journal.GetSection("AfterSnapshot").Set(JsonSerializer.Serialize(current));
        journal.GetSection("State").Set("Prepared");
        var persistedBeforeValidation = File.ReadAllText(path);

        await Assert.That(() => new TaskSourceManager(
                configuration,
                new RecordingTaskStorageBuilder(),
                defaultStoragePathProvider: () => defaultPath))
            .Throws<TaskSpaceCatalogException>();

        await Assert.That(File.ReadAllText(path)).IsEqualTo(persistedBeforeValidation);
    }

    [Test]
    public async Task SourceManager_CorruptDescriptorSlotFailsAsCatalogErrorBeforePersistence()
    {
        var path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        var configuration = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        var defaultPath = Path.Combine(_rootPath, "default");
        _ = TaskSourceSettingsAdapter.LoadOrCreate(configuration, defaultPath);
        configuration.GetSection(TaskSourcesSettings.SectionName)
            .GetSection("SourceEntry0")
            .Set("{not-json");
        var persistedBeforeValidation = File.ReadAllText(path);

        await Assert.That(() => new TaskSourceManager(
                configuration,
                new RecordingTaskStorageBuilder(),
                defaultStoragePathProvider: () => defaultPath))
            .Throws<TaskSpaceCatalogException>();

        await Assert.That(File.ReadAllText(path)).IsEqualTo(persistedBeforeValidation);
    }

    [Test]
    public async Task SourceManager_CorruptSyncProfilesFailAsCatalogErrorBeforePersistence()
    {
        var path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        var configuration = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        var defaultPath = Path.Combine(_rootPath, "default");
        _ = TaskSourceSettingsAdapter.LoadOrCreate(configuration, defaultPath);
        configuration.Set("TaskSourceSyncProfiles", "{not-json");
        var persistedBeforeValidation = File.ReadAllText(path);

        await Assert.That(() => new TaskSourceManager(
                configuration,
                new RecordingTaskStorageBuilder(),
                defaultStoragePathProvider: () => defaultPath))
            .Throws<TaskSpaceCatalogException>();

        await Assert.That(File.ReadAllText(path)).IsEqualTo(persistedBeforeValidation);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("not-a-url")]
    [Arguments("ftp://tasks.example")]
    [Arguments("https:///missing-host")]
    [Arguments("https://example..com")]
    public async Task SourceManager_ServerSettingsRejectInvalidEndpoint(string url)
    {
        var configuration = CreateConfiguration();
        var manager = new TaskSourceManager(
            configuration,
            new RecordingTaskStorageBuilder(),
            defaultStoragePathProvider: () => Path.Combine(_rootPath, "default"));
        var server = manager.AddConfiguredLocalSource("Server", Path.Combine(_rootPath, "server"));

        await Assert.That(() => manager.PersistSourceSettings(CreateServerDraft(
                server.Id,
                url,
                "example-user")))
            .Throws<InvalidOperationException>();

        await Assert.That(manager.GetSourceSettings(server.Id).Storage.IsServerMode).IsFalse();
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private IConfigurationRoot CreateConfiguration()
    {
        var path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        var configuration = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        return configuration;
    }

    private static TaskSpaceSettingsDraft CreateServerDraft(
        string sourceId,
        string url,
        string login) =>
        new()
        {
            SourceId = sourceId,
            Storage = new TaskStorageSettings
            {
                IsServerMode = true,
                URL = url,
                Login = login,
                Password = "password"
            },
            Git = new GitSettings()
        };

    private sealed class RecordingStorage : IStorage
    {
        public List<string?> SavedTaskIds { get; } = [];
        public int DisconnectCalls { get; private set; }

        public event EventHandler<TaskStorageUpdateEventArgs>? Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public Task<TaskItem> Save(TaskItem taskItem)
        {
            SavedTaskIds.Add(taskItem.Id);
            return Task.FromResult(taskItem);
        }

        public Task<bool> Remove(string itemId) => Task.FromResult(true);
        public Task<TaskItem?> Load(string itemId) => Task.FromResult<TaskItem?>(null);
        public async IAsyncEnumerable<TaskItem> GetAll() { yield break; }
        public Task BulkInsert(IEnumerable<TaskItem> taskItems) => Task.CompletedTask;
        public Task<bool> Connect() => Task.FromResult(true);
        public Task Disconnect()
        {
            DisconnectCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTaskStorageBuilder : ITaskStorageBuilder
    {
        public List<(TaskSourceDescriptor Descriptor, RecordingStorage Storage)> Builds { get; } = [];
        public string? ThrowForSourceId { get; set; }

        public TaskStorageBuildResult Build(TaskStorageBuildRequest request)
        {
            if (string.Equals(request.Descriptor.Id, ThrowForSourceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Build failed.");
            }

            var storage = new RecordingStorage();
            var descriptor = new TaskSourceDescriptor
            {
                Id = request.Descriptor.Id,
                DisplayName = request.Descriptor.DisplayName,
                Kind = request.Descriptor.Kind,
                Path = request.Descriptor.Path,
                Url = request.Descriptor.Url,
                IsEnabled = request.Descriptor.IsEnabled
            };
            Builds.Add((descriptor, storage));

            return new TaskStorageBuildResult(
                new UnifiedTaskStorage(new TaskTreeManager(storage), request.TaskContext),
                watcher: null);
        }
    }
}
