using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using Unlimotion.Domain;
using Unlimotion.Notes.Areas;
using Unlimotion.Notes.Conflicts;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedAuxiliaryUiTests
{
    [Test]
    public async Task AreaManagement_CrudCycleArchiveAndCompactLayout_PreserveCatalog()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var store = new AreaCatalogStore(new FileNoteVault(directory.Path));
            using var viewModel = new AreaManagementViewModel(store) { IsOpen = true };
            await viewModel.LoadAsync();

            var work = await viewModel.CreateRootAsync("Работа", "Заметки/Работа");
            var project = await viewModel.CreateChildAsync(work.Id, "Проект");
            await viewModel.RenameAsync(project.Id, "Проект Alpha");
            await viewModel.ReparentAsync(project.Id, parentId: null);
            await viewModel.ReparentAsync(project.Id, work.Id);
            await viewModel.ArchiveAsync(work.Id);

            var cycle = await NotesTestSupport.CaptureAsync<InvalidDataException>(() =>
                viewModel.ReparentAsync(work.Id, project.Id));
            var afterRejectedCycle = await store.LoadAsync();
            await viewModel.RestoreAsync(work.Id);
            var persisted = await store.LoadAsync();

            var view = new AreaManagement { DataContext = viewModel };
            var window = new Window { Width = 620, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var list = FindControl<ListBox>(view, "AreaManagementList");
                var editor = FindControl<Border>(view, "AreaManagementEditor");

                using (Assert.Multiple())
                {
                    await Assert.That(cycle.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase)).IsTrue();
                    await Assert.That(afterRejectedCycle.Catalog.Areas.Single(area => area.Id == work.Id).ParentId).IsNull();
                    await Assert.That(persisted.Catalog.Areas.Count).IsEqualTo(2);
                    await Assert.That(persisted.Catalog.Areas.Single(area => area.Id == work.Id).IsArchived).IsFalse();
                    await Assert.That(persisted.Catalog.Areas.Single(area => area.Id == project.Id).Name).IsEqualTo("Проект Alpha");
                    await Assert.That(persisted.Catalog.Areas.Single(area => area.Id == project.Id).ParentId).IsEqualTo(work.Id);
                    await Assert.That(persisted.Catalog.Areas.Single(area => area.Id == work.Id).DefaultNoteFolder).IsEqualTo("Заметки/Работа");
                    await Assert.That(list.IsVisible).IsTrue();
                    await Assert.That(Grid.GetRow(editor)).IsEqualTo(1);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(true, "AreaManagementUseLocalButton", "Локальная версия")]
    [Arguments(false, "AreaManagementUseExternalButton", "Внешняя версия")]
    public async Task AreaManagement_ExternalChangeWhileFormIsDirtyRequiresExplicitResolution(
        bool keepLocal,
        string actionAutomationId,
        string expectedName)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            var store = new AreaCatalogStore(vault);
            using var viewModel = new AreaManagementViewModel(store) { IsOpen = true };
            await viewModel.LoadAsync();
            var area = await viewModel.CreateRootAsync("Исходная");
            viewModel.SelectedArea = viewModel.Areas.Single(value => value.Id == area.Id);
            viewModel.DraftName = "Локальная версия";

            var external = await new AreaCatalogStore(vault).LoadAsync();
            external.Catalog.Areas.Single(value => value.Id == area.Id).Name = "Внешняя версия";
            _ = await new AreaCatalogStore(vault).SaveAsync(external.Catalog, external.Revision);
            await viewModel.LoadAsync();

            var view = new AreaManagement { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var conflict = FindControl<Border>(view, "AreaManagementExternalConflict");
                var action = FindControl<Button>(view, actionAutomationId);
                await Assert.That(conflict.IsEffectivelyVisible).IsTrue();
                await Assert.That(viewModel.DraftName).IsEqualTo("Локальная версия");

                RaiseClick(action);
                await Assert.That(WaitFor(() => !viewModel.HasExternalConflict && !viewModel.IsBusy)).IsTrue();
                var persisted = await store.LoadAsync();
                await Assert.That(persisted.Catalog.Areas.Single(value => value.Id == area.Id).Name)
                    .IsEqualTo(expectedName);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task FilesDrawer_ListsOnlyThematicMarkdown_AndUsesOpenCallback()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            await vault.CreateAsync("Ежедневные/2026-08-24.md", "daily\n");
            await vault.CreateAsync(".unlimotion/internal.md", "internal\n");
            await vault.CreateAsync("Проекты/Alpha.md", "alpha\n");
            await vault.CreateAsync("Справка.md", "reference\n");

            using var viewModel = new FeedFilesDrawerViewModel(vault) { IsOpen = true };
            string? openedPath = null;
            viewModel.OpenFileCallbackAsync = path =>
            {
                openedPath = path;
                return Task.CompletedTask;
            };
            await viewModel.RefreshAsync();

            var view = new FeedFilesDrawer { DataContext = viewModel };
            var window = new Window { Width = 360, Height = 600, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var list = FindControl<ListBox>(view, "FeedFilesList");
                var firstOpenButton = view.GetVisualDescendants()
                    .OfType<Button>()
                    .First(button => AutomationProperties.GetAutomationId(button) == "FeedFileOpenButton");
                var selectedFile = firstOpenButton.DataContext as FeedFileItemViewModel;
                RaiseClick(firstOpenButton);
                await Assert.That(WaitFor(() => openedPath is not null)).IsTrue();

                using (Assert.Multiple())
                {
                    await Assert.That(viewModel.Files.Select(file => file.RelativePath)).IsEquivalentTo([
                        "Проекты/Alpha.md",
                        "Справка.md"
                    ]);
                    await Assert.That(openedPath).IsEqualTo(selectedFile?.RelativePath);
                    await Assert.That(viewModel.IsOpen).IsFalse();
                    await Assert.That(list.ItemCount).IsEqualTo(2);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(DocumentConflictResolution.UseEditor, "FeedConflictUseEditorButton")]
    [Arguments(DocumentConflictResolution.UseDisk, "FeedConflictUseDiskButton")]
    [Arguments(DocumentConflictResolution.SaveBoth, "FeedConflictSaveBothButton")]
    public async Task ConflictSurface_ExposesAndExecutesAllThreeNonModalActions(
        DocumentConflictResolution resolution,
        string actionAutomationId)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            const string relativePath = "Темы/Идея.md";
            var original = await vault.CreateAsync(relativePath, "base\n");
            var presentation = new RecordingDocumentExternalChangeSink();
            var dirty = new InMemoryDirtyDocumentRegistry();
            await using var coordinator = new DocumentConflictCoordinator(
                "vault1",
                vault,
                dirty,
                presentation,
                new MemoryFeedDraftStore(),
                new MemoryRevisionStore(),
                new MemoryDocumentConflictStore(),
                new OwnWriteRegistry());
            dirty.Set(new DirtyDocumentBuffer(relativePath, "editor\n", "editor", 0, original.Revision, false));
            await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk\n", new UTF8Encoding(false));
            var disk = await vault.ReadAsync(relativePath);
            await coordinator.HandleAsync(new VaultWatchChange(
                VaultWatchScope.Markdown,
                VaultWatchChangeKind.Changed,
                relativePath,
                null,
                disk!.Revision), CancellationToken.None);

            using var viewModel = new FeedDocumentConflictViewModel(coordinator, presentation.Conflicts.Single());
            var view = new FeedDocumentConflict { DataContext = viewModel };
            var host = new Grid { Children = { new TextBlock { Text = "Feed remains visible" }, view } };
            var window = new Window { Width = 860, Height = 620, Content = host };
            try
            {
                window.Show();
                RunLayoutJobs();
                var editorButton = FindControl<Button>(view, "FeedConflictUseEditorButton");
                var diskButton = FindControl<Button>(view, "FeedConflictUseDiskButton");
                var bothButton = FindControl<Button>(view, "FeedConflictSaveBothButton");
                var selectedButton = FindControl<Button>(view, actionAutomationId);
                RaiseClick(selectedButton);

                await Assert.That(WaitFor(() => viewModel.IsResolved)).IsTrue();
                using (Assert.Multiple())
                {
                    await Assert.That(editorButton).IsNotNull();
                    await Assert.That(diskButton).IsNotNull();
                    await Assert.That(bothButton).IsNotNull();
                    await Assert.That(viewModel.ResolutionResult!.Resolution).IsEqualTo(resolution);
                    await Assert.That(viewModel.IsOpen).IsFalse();
                    await Assert.That(host.Children.Count).IsEqualTo(2);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskClassification_MultipleAreasAndGoal_AutosaveAndRenderChips()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var storage = new RecordingClassificationTaskStorage();
            using var task = new TaskItemViewModel(new TaskItem
            {
                Id = "task-1",
                Title = "Task",
                AreaIds = [],
                ContainsTasks = [],
                ParentTasks = [],
                BlocksTasks = [],
                BlockedByTasks = []
            }, storage, () => true)
            {
                PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromMilliseconds(20)
            };
            using var editor = TaskClassificationEditorViewModel.ForTask(task, [
                new TaskClassificationAreaDefinition("work", "Работа"),
                new TaskClassificationAreaDefinition("product", "Продукт")
            ]);
            var view = new TaskClassificationControl
            {
                Editor = editor,
                AutomationIdPrefix = TaskClassificationControl.FeedAutomationIdPrefix
            };
            var window = new Window { Width = 420, Height = 360, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                editor.TrySetGoal(true);
                editor.TrySetAreaSelected("work", true);
                editor.TrySetAreaSelected("product", true);
                await Task.Delay(120);
                RunLayoutJobs();

                var goal = FindControl<CheckBox>(view, "FeedTaskClassificationGoalCheckBox");
                var chips = FindControl<ItemsControl>(view, "FeedTaskClassificationSelectedAreaChips");
                using (Assert.Multiple())
                {
                    await Assert.That(task.IsGoal).IsTrue();
                    await Assert.That(task.AreaIds).IsEquivalentTo(["work", "product"]);
                    await Assert.That(editor.SelectedAreas.Count).IsEqualTo(2);
                    await Assert.That(storage.Snapshots.Any(snapshot =>
                        snapshot.IsGoal && snapshot.AreaIds.Contains("work") && snapshot.AreaIds.Contains("product"))).IsTrue();
                    await Assert.That(goal.IsChecked).IsTrue();
                    await Assert.That(chips.ItemCount).IsEqualTo(2);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskClassification_UnsupportedServer_DisablesUnsafeEditingWithExplanation()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var editor = TaskClassificationEditorViewModel.ForDraft(
                isGoal: false,
                selectedAreaIds: [],
                areas: [new TaskClassificationAreaDefinition("work", "Работа")],
                supportsEditing: static () => false);
            var view = new TaskClassificationControl
            {
                Editor = editor,
                AutomationIdPrefix = TaskClassificationControl.FeedAutomationIdPrefix
            };
            var window = new Window { Width = 420, Height = 320, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var goal = FindControl<CheckBox>(view, "FeedTaskClassificationGoalCheckBox", requireEnabled: false);
                var areas = FindControl<Expander>(view, "FeedTaskClassificationAreaPickerExpander", requireEnabled: false);
                var explanation = FindControl<TextBlock>(view, "FeedTaskClassificationBlockedExplanation", requireEnabled: false);

                using (Assert.Multiple())
                {
                    await Assert.That(goal.IsEnabled).IsFalse();
                    await Assert.That(areas.IsEnabled).IsFalse();
                    await Assert.That(explanation.IsVisible).IsTrue();
                    await Assert.That(explanation.Text).IsNotEmpty();
                    await Assert.That(editor.TrySetGoal(true)).IsFalse();
                    await Assert.That(editor.TrySetAreaSelected("work", true)).IsFalse();
                    await Assert.That(editor.IsGoal).IsFalse();
                    await Assert.That(editor.SelectedAreas).IsEmpty();
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_UsesReusableClassificationControl()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                owner.DetailsAreOpen = true;
                var task = TestHelpers.SetCurrentTask(owner, MainWindowViewModelFixture.RootTask2Id);
                var view = new MainControl { DataContext = owner };
                window = new Window { Width = 1600, Height = 1400, Content = view };
                window.Show();
                RunLayoutJobs();

                var classification = view.GetVisualDescendants()
                    .OfType<TaskClassificationControl>()
                    .FirstOrDefault();
                await Assert.That(classification).IsNotNull();
                using (Assert.Multiple())
                {
                    await Assert.That(classification!.Task).IsSameReferenceAs(task);
                    await Assert.That(classification.Owner).IsSameReferenceAs(owner);
                    await Assert.That(classification.EffectiveEditor).IsNotNull();
                    await Assert.That(FindControl<CheckBox>(
                        classification,
                        "CurrentTaskClassificationGoalCheckBox")).IsNotNull();
                }
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static T FindControl<T>(Control root, string automationId, bool requireEnabled = true)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(AutomationProperties.GetAutomationId(candidate), automationId, StringComparison.Ordinal)
                && candidate.IsAttachedToVisualTree()
                && (!requireEnabled || candidate.IsEnabled));
        return control ?? throw new InvalidOperationException($"Control '{automationId}' was not found.");
    }

    private static void RaiseClick(Button button)
    {
        if (!button.IsEnabled)
        {
            return;
        }

        if (button.Command is { } command)
        {
            if (command.CanExecute(button.CommandParameter))
            {
                command.Execute(button.CommandParameter);
            }
        }
        else
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

        RunLayoutJobs();
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 5000) =>
        SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

    private static void RunLayoutJobs()
    {
        for (var index = 0; index < 20; index++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class RecordingClassificationTaskStorage : ITaskStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);
        public ITaskRelationsIndex Relations => null!;
        public TaskTreeManager TaskTreeManager => null!;
        public List<TaskItem> Snapshots { get; } = new();
        public event EventHandler<EventArgs>? Initiated;

        public Task Init() => Task.CompletedTask;
        public Task<TaskItemViewModel> Add(TaskItemViewModel? currentTask = null, bool isBlocked = false) => throw new NotSupportedException();
        public Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask) => throw new NotSupportedException();
        public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true) => throw new NotSupportedException();
        public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent) => throw new NotSupportedException();
        public Task<TaskItemViewModel> Update(TaskItemViewModel change)
        {
            Snapshots.Add(change.Model);
            return Task.FromResult(change);
        }

        public Task<TaskItemViewModel> Update(TaskItem change) => throw new NotSupportedException();
        public Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents) => throw new NotSupportedException();
        public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents) => throw new NotSupportedException();
        public Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask) => throw new NotSupportedException();
        public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) => throw new NotSupportedException();
        public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) => throw new NotSupportedException();
        public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) => throw new NotSupportedException();
    }
}
