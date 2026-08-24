using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Domain;
using Unlimotion.Notes.Identity;
using Unlimotion.Notes.Areas;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Search;
using Unlimotion.Notes.Vault;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedControlUiTests
{
    [Test]
    public async Task Feed_WithoutVault_ShowsOnboardingAndInvokesFolderChoice()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var viewModel = new FeedViewModel();
            var callbackInvoked = false;
            viewModel.ChooseVaultAsync = () =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            };

            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                var onboarding = FindControlByAutomationId<Border>(view, "FeedOnboardingRoot");
                var chooseButton = FindControlByAutomationId<Button>(view, "FeedOnboardingChooseVaultButton");

                await Assert.That(onboarding.IsVisible).IsTrue();
                InvokeButton(chooseButton);

                await Assert.That(WaitFor(() => callbackInvoked)).IsTrue();
                await Assert.That(viewModel.IsVaultInitialized).IsFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_WithoutOverlayModels_HidesBlockingSurfaces()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var viewModel = new FeedViewModel();
            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                var documentConflict = FindControlByAutomationId<Border>(view, "FeedDocumentConflictRoot");
                var identityConflict = FindControlByAutomationId<Border>(view, "FeedIdentityConflictRoot");
                var reviewRecovery = FindControlByAutomationId<Border>(view, "FeedReviewRecoveryRoot");

                using (Assert.Multiple())
                {
                    await Assert.That(documentConflict.IsEffectivelyVisible).IsFalse();
                    await Assert.That(identityConflict.IsEffectivelyVisible).IsFalse();
                    await Assert.That(reviewRecovery.IsEffectivelyVisible).IsFalse();
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_MarkerlessHeadingWithUniqueCatalogName_UsesOnlyStableAreaId()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            var today = new DateOnly(2026, 8, 24);
            directory.WriteDaily(today, "## Работа\n- [ ] Уникальное сопоставление области\n");
            await SeedAreaCatalogAsync(directory.Path,
                new AreaDefinition { Id = "work", Name = "Работа" });

            var taskTarget = new RecordingFeedTaskTarget();
            using var viewModel = new FeedViewModel(() => today)
            {
                TaskCreationTarget = taskTarget
            };
            await viewModel.InitializeVaultAsync(directory.Path);
            viewModel.StartReviewCommand.Execute(null);
            await Assert.That(await WaitForAsync(() => viewModel.IsReviewSelectionVisible)).IsTrue();

            var matchingAreas = viewModel.Areas
                .Where(area => string.Equals(area.DisplayName, "Работа", StringComparison.Ordinal))
                .ToArray();
            var selectedTaskAreas = viewModel.ReviewTaskAreas
                .Where(static area => area.IsSelected)
                .Select(static area => area.Area.Identity)
                .ToArray();
            using (Assert.Multiple())
            {
                await Assert.That(matchingAreas.Length).IsEqualTo(1);
                await Assert.That(matchingAreas[0].Identity).IsEqualTo("work");
                await Assert.That(viewModel.Areas.Any(area => area.Identity.StartsWith("area-", StringComparison.Ordinal))).IsFalse();
                await Assert.That(viewModel.ReviewDestinationArea?.Identity).IsEqualTo("work");
                await Assert.That(selectedTaskAreas).IsEquivalentTo(["work"]);
            }

            viewModel.CreateTaskCommand.Execute(null);
            await Assert.That(await WaitForAsync(() => viewModel.HasCreatedTask && !viewModel.IsBusy)).IsTrue();
            await Assert.That(taskTarget.Tasks.Count).IsEqualTo(1);
            await Assert.That(taskTarget.Tasks.Single().AreaIds).IsEquivalentTo(["work"]);
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_MarkerlessHeadingWithAmbiguousCatalogName_RemainsNoArea()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            var today = new DateOnly(2026, 8, 24);
            directory.WriteDaily(today, "## Проект\n- [ ] Не угадывать неоднозначную область\n");
            await SeedAreaCatalogAsync(
                directory.Path,
                new AreaDefinition { Id = "work-project", Name = "Проект" },
                new AreaDefinition { Id = "home-project", Name = "Проект" });

            var taskTarget = new RecordingFeedTaskTarget();
            using var viewModel = new FeedViewModel(() => today)
            {
                TaskCreationTarget = taskTarget
            };
            await viewModel.InitializeVaultAsync(directory.Path);
            viewModel.SearchQuery = "Не угадывать неоднозначную область";
            await Assert.That(await WaitForAsync(() => viewModel.SearchResults.Count == 1)).IsTrue();
            viewModel.SelectedSearchArea = viewModel.SearchAreaOptions.Single(area =>
                string.Equals(area.AreaIdentity, "work-project", StringComparison.Ordinal));
            await Assert.That(await WaitForAsync(() => viewModel.SearchResults.Count == 0)).IsTrue();
            viewModel.SelectedSearchArea = FeedSearchAreaOptionViewModel.NoArea;
            await Assert.That(await WaitForAsync(() => viewModel.SearchResults.Count == 1)).IsTrue();

            viewModel.StartReviewCommand.Execute(null);
            await Assert.That(await WaitForAsync(() => viewModel.IsReviewSelectionVisible)).IsTrue();
            using (Assert.Multiple())
            {
                await Assert.That(viewModel.Areas.Count(area =>
                    string.Equals(area.DisplayName, "Проект", StringComparison.Ordinal))).IsEqualTo(3);
                await Assert.That(viewModel.Areas.Count(area =>
                    string.Equals(area.DisplayName, "Проект", StringComparison.Ordinal)
                    && area.HasStableAreaId)).IsEqualTo(2);
                await Assert.That(viewModel.Areas.Count(area =>
                    string.Equals(area.DisplayName, "Проект", StringComparison.Ordinal)
                    && !area.HasStableAreaId
                    && area.IsExistingHeadingDestination)).IsEqualTo(1);
                await Assert.That(viewModel.Areas.Any(area => area.Identity.StartsWith("area-", StringComparison.Ordinal))).IsFalse();
                await Assert.That(viewModel.ReviewDestinationArea?.DisplayName).IsEqualTo("Проект");
                await Assert.That(viewModel.ReviewDestinationArea?.StableAreaId).IsNull();
                await Assert.That(viewModel.ReviewTaskAreas.Any(static area => area.IsSelected)).IsFalse();
                await Assert.That(viewModel.SearchResults.Single().DisplayAreas).IsEqualTo(viewModel.Areas[0].DisplayName);
            }

            viewModel.CreateTaskCommand.Execute(null);
            await Assert.That(await WaitForAsync(() => viewModel.HasCreatedTask && !viewModel.IsBusy)).IsTrue();
            await Assert.That(taskTarget.Tasks.Count).IsEqualTo(1);
            await Assert.That(taskTarget.Tasks.Single().AreaIds).IsEmpty();
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_MarkerlessHeadingWithoutCatalogMatch_RemainsPhysicalDestinationWithoutMetadata()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            var today = new DateOnly(2026, 8, 24);
            directory.WriteDaily(today, "## Неизвестная область\n- [ ] Не создавать синтетическую область\n");
            await SeedAreaCatalogAsync(directory.Path,
                new AreaDefinition { Id = "work", Name = "Работа" });

            using var viewModel = new FeedViewModel(() => today);
            await viewModel.InitializeVaultAsync(directory.Path);
            viewModel.StartReviewCommand.Execute(null);
            await Assert.That(await WaitForAsync(() => viewModel.IsReviewSelectionVisible)).IsTrue();

            using (Assert.Multiple())
            {
                await Assert.That(viewModel.Areas.Count(area =>
                    string.Equals(area.DisplayName, "Неизвестная область", StringComparison.Ordinal))).IsEqualTo(1);
                await Assert.That(viewModel.Areas.Any(area =>
                    area.Identity.StartsWith("area-", StringComparison.Ordinal))).IsFalse();
                await Assert.That(viewModel.ReviewDestinationArea?.DisplayName).IsEqualTo("Неизвестная область");
                await Assert.That(viewModel.ReviewDestinationArea?.StableAreaId).IsNull();
                await Assert.That(viewModel.ReviewDestinationArea?.IsExistingHeadingDestination).IsTrue();
                await Assert.That(viewModel.ReviewTaskAreas.Any(static area => area.IsSelected)).IsFalse();
            }

            viewModel.ReviewNoteTitle = "Неизвестная классификация";
            viewModel.ReviewNoteFolder = "Темы";
            viewModel.CreateNoteCommand.Execute(null);
            var notePath = System.IO.Path.Combine(directory.Path, "Темы", "Неизвестная классификация.md");
            await Assert.That(await WaitForAsync(() => File.Exists(notePath) && !viewModel.IsBusy)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(notePath)).Contains("unlimotion-areas: []");
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_ArchivedExplicitAreaRemainsDestinationButIsNotInheritedAsClassification()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            var today = new DateOnly(2026, 8, 24);
            directory.WriteDaily(
                today,
                "## Архив <!-- unlimotion-area:archived -->\n- [ ] Не наследовать архивную область\n");
            await SeedAreaCatalogAsync(directory.Path,
                new AreaDefinition { Id = "archived", Name = "Архив", IsArchived = true });
            var taskTarget = new RecordingFeedTaskTarget();
            using var viewModel = new FeedViewModel(() => today)
            {
                TaskCreationTarget = taskTarget
            };

            await viewModel.InitializeVaultAsync(directory.Path);
            viewModel.StartReviewCommand.Execute(null);
            await Assert.That(await WaitForAsync(() => viewModel.IsReviewSelectionVisible)).IsTrue();

            var physicalDestination = viewModel.Areas.Single(area => area.StableAreaId == "archived");
            using (Assert.Multiple())
            {
                await Assert.That(physicalDestination.IsExistingHeadingDestination).IsTrue();
                await Assert.That(physicalDestination.IsClassificationSelectable).IsFalse();
                await Assert.That(physicalDestination.DestinationDisplayName).Contains(viewModel.Areas[0].DisplayName);
                await Assert.That(viewModel.ReviewDestinationArea).IsSameReferenceAs(physicalDestination);
                await Assert.That(viewModel.ReviewTaskAreas.Any(area =>
                    area.Area.StableAreaId == "archived")).IsFalse();
                await Assert.That(viewModel.SearchAreaOptions.Any(area =>
                    area.AreaIdentity == "archived")).IsFalse();
            }

            viewModel.CreateTaskCommand.Execute(null);
            await Assert.That(await WaitForAsync(() => viewModel.HasCreatedTask && !viewModel.IsBusy)).IsTrue();
            await Assert.That(taskTarget.Tasks.Count).IsEqualTo(1);
            await Assert.That(taskTarget.Tasks.Single().AreaIds).IsEmpty();
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_SearchFiltersAndOpensExactDailyAndPermanentBlocks()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            directory.WriteDaily(
                new DateOnly(2026, 8, 22),
                "## Работа <!-- unlimotion-area:work -->\nСтарая уникальная запись\n");
            directory.WriteDaily(
                new DateOnly(2026, 8, 24),
                "## Дом <!-- unlimotion-area:home -->\nНе та уникальная запись\n\n"
                + "## Работа <!-- unlimotion-area:work -->\nКонтекст перед целью\n\nУникальная новая запись\n");
            directory.WriteNote(
                "Темы/Справка.md",
                "---\nunlimotion-areas: [work]\n---\nВводный блок\n\nПостоянная уникальная справка\n");
            await SeedAreaCatalogAsync(
                directory.Path,
                new AreaDefinition { Id = "work", Name = "Работа" },
                new AreaDefinition { Id = "home", Name = "Дом" });
            using var viewModel = new FeedViewModel(() => new DateOnly(2026, 8, 24));
            await viewModel.InitializeVaultAsync(directory.Path);

            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                var chronology = FindControlByAutomationId<ListBox>(view, "FeedChronologyList");
                var searchBox = FindControlByAutomationId<TextBox>(view, "FeedSearchBox");
                var searchResults = FindControlByAutomationId<ListBox>(view, "FeedSearchResultsList");
                var typePicker = FindControlByAutomationId<ComboBox>(view, "FeedSearchTypePicker");
                var areaPicker = FindControlByAutomationId<ComboBox>(view, "FeedSearchAreaPicker");
                var fromPicker = FindControlByAutomationId<DatePicker>(view, "FeedSearchFromDatePicker");
                var toPicker = FindControlByAutomationId<DatePicker>(view, "FeedSearchToDatePicker");

                await Assert.That(viewModel.Days.Count).IsEqualTo(2);
                await Assert.That(viewModel.Days[0].Date).IsEqualTo(new DateOnly(2026, 8, 24));
                await Assert.That(chronology.IsVisible).IsTrue();

                searchBox.Text = "уникальная";
                typePicker.SelectedItem = viewModel.SearchTypeOptions.Single(option =>
                    option.Type == FeedSearchDocumentType.Daily);
                areaPicker.SelectedItem = viewModel.SearchAreaOptions.Single(option =>
                    string.Equals(option.AreaIdentity, "work", StringComparison.Ordinal));
                fromPicker.SelectedDate = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
                toPicker.SelectedDate = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
                RunLayoutJobs();

                await Assert.That(WaitFor(() => viewModel.SearchResults.Count == 1)).IsTrue();
                await Assert.That(chronology.IsEffectivelyVisible).IsFalse();
                await Assert.That(searchResults.IsEffectivelyVisible).IsTrue();
                await Assert.That(typePicker.IsEffectivelyVisible).IsTrue();
                await Assert.That(areaPicker.IsEffectivelyVisible).IsTrue();
                await Assert.That(viewModel.SearchResults[0].Date).IsEqualTo(new DateOnly(2026, 8, 24));
                await Assert.That(viewModel.SearchResults[0].Entry.AreaIdentities).Contains("work");
                await Assert.That(viewModel.SearchResults[0].DisplayAreas).IsEqualTo("Работа");
                await Assert.That(viewModel.SearchResults[0].RelativePath).IsEqualTo("Ежедневные/2026-08-24.md");

                var dailyResult = viewModel.SearchResults[0];
                var dailyPreviewId = viewModel.Days[0].MarkdownEditor.Blocks
                    .Single(block => block.Index == dailyResult.Entry.BlockIndex)
                    .PreviewAutomationId;
                InvokeButton(FindControlByAutomationId<Button>(view, dailyResult.AutomationId));

                await Assert.That(WaitFor(() =>
                    string.IsNullOrEmpty(viewModel.SearchQuery)
                    && ReferenceEquals(viewModel.SelectedDay, viewModel.Days[0])
                    && FindOptionalControlByAutomationId<Control>(view, dailyPreviewId) is { } preview
                    && (preview.IsFocused || ReferenceEquals(window.FocusManager?.GetFocusedElement(), preview)))).IsTrue();

                searchBox.Text = "постоянная уникальная";
                typePicker.SelectedItem = viewModel.SearchTypeOptions.Single(option =>
                    option.Type == FeedSearchDocumentType.Note);
                fromPicker.SelectedDate = null;
                toPicker.SelectedDate = null;
                RunLayoutJobs();

                await Assert.That(WaitFor(() => viewModel.SearchResults.Count == 1)).IsTrue();
                var noteResult = viewModel.SearchResults[0];
                await Assert.That(noteResult.Type).IsEqualTo(FeedSearchDocumentType.Note);
                await Assert.That(noteResult.RelativePath).IsEqualTo("Темы/Справка.md");

                InvokeButton(FindControlByAutomationId<Button>(view, noteResult.AutomationId));
                await Assert.That(WaitFor(() =>
                    string.IsNullOrEmpty(viewModel.SearchQuery)
                    && viewModel.OpenedThematicFile?.RelativePath == "Темы/Справка.md"
                    && viewModel.OpenedThematicFile.MarkdownEditor.Blocks
                        .Any(block => block.Index == noteResult.Entry.BlockIndex
                            && FindOptionalControlByAutomationId<Control>(view, block.PreviewAutomationId) is { } preview
                            && (preview.IsFocused || ReferenceEquals(window.FocusManager?.GetFocusedElement(), preview))))).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_ClearingSearchRestoresChronologyScrollAndSelection()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            var today = new DateOnly(2026, 8, 24);
            for (var offset = 0; offset < 18; offset++)
            {
                directory.WriteDaily(
                    today.AddDays(-offset),
                    $"## Работа <!-- unlimotion-area:work -->\nЗапись {offset}\n\nДополнительный контекст {offset}\n");
            }

            using var viewModel = new FeedViewModel(() => today);
            await viewModel.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 780, Height = 460, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                var chronology = FindControlByAutomationId<ListBox>(view, "FeedChronologyList");
                var scrollViewer = chronology.GetVisualDescendants().OfType<ScrollViewer>().Single();
                var selected = viewModel.Days[9];
                chronology.SelectedItem = selected;
                chronology.ScrollIntoView(selected);
                RunLayoutJobs();
                scrollViewer.Offset = new Avalonia.Vector(0, Math.Min(260, scrollViewer.ScrollBarMaximum.Y));
                RunLayoutJobs();
                var savedOffset = scrollViewer.Offset.Y;
                await Assert.That(savedOffset).IsGreaterThan(0);

                var searchBox = FindControlByAutomationId<TextBox>(view, "FeedSearchBox");
                searchBox.Text = "нет такого совпадения";
                await Assert.That(WaitFor(() => viewModel.IsSearchActive)).IsTrue();
                searchBox.Text = string.Empty;

                await Assert.That(WaitFor(() =>
                    !viewModel.IsSearchActive
                    && ReferenceEquals(viewModel.SelectedDay, selected)
                    && Math.Abs(scrollViewer.Offset.Y - savedOffset) < 0.5)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_SearchOpensDailyResultOutsideInitialChronologyPage()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            var today = new DateOnly(2026, 8, 24);
            var targetDate = today.AddDays(-35);
            for (var offset = 0; offset < 40; offset++)
            {
                var body = offset == 35
                    ? "Глубокий уникальный результат"
                    : $"Обычная запись {offset}";
                directory.WriteDaily(
                    today.AddDays(-offset),
                    $"## Работа <!-- unlimotion-area:work -->\n{body}\n");
            }

            using var viewModel = new FeedViewModel(() => today);
            await viewModel.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                await Assert.That(viewModel.Days.Any(day => day.Date == targetDate)).IsFalse();
                await Assert.That(viewModel.HasMoreDays).IsTrue();
                await Assert.That(FindControlByAutomationId<Button>(view, "FeedLoadOlderDaysButton").IsEffectivelyVisible).IsTrue();

                FindControlByAutomationId<TextBox>(view, "FeedSearchBox").Text = "глубокий уникальный";
                await Assert.That(WaitFor(() => viewModel.SearchResults.Count == 1)).IsTrue();
                var result = viewModel.SearchResults[0];
                await Assert.That(result.Date).IsEqualTo(targetDate);

                InvokeButton(FindControlByAutomationId<Button>(view, result.AutomationId));

                await Assert.That(WaitFor(() =>
                    string.IsNullOrEmpty(viewModel.SearchQuery)
                    && viewModel.SelectedDay?.Date == targetDate
                    && viewModel.SelectedDay.MarkdownEditor.Blocks
                        .Any(block => block.Index == result.Entry.BlockIndex
                            && FindOptionalControlByAutomationId<Control>(view, block.PreviewAutomationId) is { } preview
                            && (preview.IsFocused || ReferenceEquals(window.FocusManager?.GetFocusedElement(), preview))),
                    timeoutMilliseconds: 8000)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_TaskSearchResultNavigatesToExactTaskCard()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            using var directory = new FeedTempDirectory();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var target = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask2Id)
                    ?? throw new InvalidOperationException("The search task fixture is missing.");
                var feed = owner.Feed;
                feed.TaskOwner = owner;
                feed.TaskResolver = taskId => owner.taskRepository?.Tasks.Items.FirstOrDefault(task =>
                    string.Equals(task.Id, taskId, StringComparison.Ordinal));
                feed.NavigateToTaskRequested = task =>
                {
                    owner.CurrentTaskItem = task;
                    owner.SelectedWorkspaceMode = WorkspaceMode.Tasks;
                    owner.DetailsAreOpen = true;
                    owner.SelectCurrentTask();
                };
                await feed.InitializeVaultAsync(directory.Path);
                owner.SelectedWorkspaceMode = WorkspaceMode.Feed;

                var view = new FeedControl { DataContext = feed };
                window = new Window { Width = 900, Height = 700, Content = view };
                window.Show();
                RunLayoutJobs();

                FindControlByAutomationId<TextBox>(view, "FeedSearchBox").Text = target.Id;
                await Assert.That(WaitFor(() =>
                    feed.SearchResults.Count == 1
                    && feed.SearchResults[0].Type == FeedSearchDocumentType.Task)).IsTrue();

                var result = feed.SearchResults[0];
                InvokeButton(FindControlByAutomationId<Button>(view, result.AutomationId));

                await Assert.That(WaitFor(() =>
                    owner.SelectedWorkspaceMode == WorkspaceMode.Tasks
                    && owner.DetailsAreOpen
                    && string.Equals(owner.CurrentTaskItem?.Id, target.Id, StringComparison.Ordinal))).IsTrue();
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_TaskReferenceRebindsAndClearDropsStaleReferencesEvenWhenAlreadyUninitialized()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            using var directory = new FeedTempDirectory();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var firstTask = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask2Id)
                    ?? throw new InvalidOperationException("The source task fixture is missing.");
                var taskId = firstTask.Id;
                var firstTaskStatus = firstTask.Status;
                var secondStorageRaw = new InMemoryStorage();
                await secondStorageRaw.Save(new TaskItem
                {
                    Id = taskId,
                    Title = "Задача из пространства B",
                    Status = DomainTaskStatus.Prepared
                });

                using var activeSecondStorage = new UnifiedTaskStorage(new TaskTreeManager(secondStorageRaw));
                await activeSecondStorage.Init();

                directory.WriteDaily(
                    new DateOnly(2026, 8, 24),
                    $"[Задача из пространства A](unlimotion://task/{taskId})\n");
                var feed = owner.Feed;
                feed.TaskOwner = owner;
                feed.TaskResolver = id => owner.taskRepository?.Tasks.Items.FirstOrDefault(task =>
                    string.Equals(task.Id, id, StringComparison.Ordinal));
                await feed.InitializeVaultAsync(directory.Path);

                var view = new FeedControl { DataContext = feed };
                window = new Window { Width = 900, Height = 700, Content = view };
                window.Show();
                RunLayoutJobs();

                var initialReference = feed.Days.Single().TaskReferences.Single();
                await Assert.That(initialReference.Task).IsSameReferenceAs(firstTask);

                await owner.BindInitializedStorage(activeSecondStorage);

                var rebound = await WaitForAsync(() =>
                {
                    var reference = feed.Days.Single().TaskReferences.Single();
                    var picker = FindOptionalControlByAutomationId<global::Unlimotion.TaskStatusPicker>(
                        view,
                        reference.StatusAutomationId);
                    return reference.Task is not null
                        && !ReferenceEquals(reference.Task, firstTask)
                        && string.Equals(reference.Task.Title, "Задача из пространства B", StringComparison.Ordinal)
                        && ReferenceEquals(picker?.Task, reference.Task);
                });

                await Assert.That(rebound).IsTrue();
                var reboundReference = feed.Days.Single().TaskReferences.Single();
                var reboundTitle = FindControlByAutomationId<Button>(view, reboundReference.TitleAutomationId);
                await Assert.That(AutomationProperties.GetName(reboundTitle)).IsEqualTo("Задача из пространства B");
                var reboundPicker = FindControlByAutomationId<global::Unlimotion.TaskStatusPicker>(
                    view,
                    reboundReference.StatusAutomationId);
                var flyout = await OpenStatusFlyoutAsync(reboundPicker);
                var inProgress = flyout.Items
                    .OfType<MenuItem>()
                    .Single(item => string.Equals(
                        AutomationProperties.GetAutomationId(item),
                        "TaskStatusOptionInProgress",
                        StringComparison.Ordinal));
                await Assert.That(inProgress.IsEnabled).IsTrue();
                inProgress.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, inProgress));

                var statusChangedInActiveSpace = await WaitForAsync(() =>
                    reboundReference.Task?.Status == DomainTaskStatus.InProgress
                    && firstTask.Status == firstTaskStatus);

                await Assert.That(statusChangedInActiveSpace).IsTrue();
                var reboundTask = reboundReference.Task
                    ?? throw new InvalidOperationException("The active task reference is missing.");

                owner.ClearTaskSpaceSurface();

                var cleared = await WaitForAsync(() =>
                {
                    var reference = feed.Days.Single().TaskReferences.Single();
                    return reference.Task is null
                        && FindOptionalControlByAutomationId<global::Unlimotion.TaskStatusPicker>(
                            view,
                            reference.StatusAutomationId) is null
                        && FindOptionalControlByAutomationId<Grid>(
                            view,
                            $"FeedTask-{taskId}-BrokenReference") is not null;
                });

                await Assert.That(cleared).IsTrue();
                await Assert.That(owner.IsInitialized).IsFalse();

                // A failed initialization can leave a storage assigned while initialization remains false.
                owner.taskRepository = activeSecondStorage;
                feed.OnTaskStorageChanged();

                var restoredWhileUninitialized = await WaitForAsync(() =>
                {
                    var reference = feed.Days.Single().TaskReferences.Single();
                    return ReferenceEquals(reference.Task, reboundTask);
                });

                await Assert.That(restoredWhileUninitialized).IsTrue();
                await Assert.That(owner.IsInitialized).IsFalse();

                owner.ClearTaskSpaceSurface();

                var clearedWhileAlreadyUninitialized = await WaitForAsync(() =>
                    feed.Days.Single().TaskReferences.Single().Task is null);

                await Assert.That(clearedWhileAlreadyUninitialized).IsTrue();
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_CtrlEnter_AppendsCaptureToSelectedArea_AndRefreshesDay()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var today = new DateOnly(2026, 8, 24);
            using var directory = new FeedTempDirectory();
            directory.WriteDaily(today, "# День\n\n## Работа\n\nНачало дня\n");
            using var viewModel = new FeedViewModel(() => today);
            await viewModel.InitializeVaultAsync(directory.Path);

            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                // The FileSystemWatcher can deliver the identity manifest's create/change events
                // after bootstrap activation. A same-ID event must not freeze the first capture.
                await Task.Delay(TimeSpan.FromMilliseconds(250));
                RunLayoutJobs();
                await Assert.That(viewModel.IsIdentityFrozen).IsFalse();

                var areaPicker = FindControlByAutomationId<ComboBox>(view, "FeedAreaPicker");
                var captureBox = FindControlByAutomationId<TextBox>(view, "FeedQuickCaptureTextBox");
                var workArea = viewModel.Areas.Single(area => area.Area?.Name == "Работа");
                areaPicker.SelectedItem = workArea;
                viewModel.QuickCaptureText = "- [ ] Подготовить прототип";
                captureBox.Focus();
                RunLayoutJobs();
                await Assert.That(captureBox.Text).IsEqualTo(viewModel.QuickCaptureText);

                var keyDown = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter,
                    PhysicalKey = PhysicalKey.Enter,
                    KeyModifiers = KeyModifiers.Control,
                    Source = captureBox
                };
                captureBox.RaiseEvent(keyDown);
                await Assert.That(keyDown.Handled).IsTrue();

                var captured = await WaitForAsync(() =>
                {
                    try
                    {
                        return File.ReadAllText(directory.GetDailyPath(today))
                                .Contains("Подготовить прототип", StringComparison.Ordinal)
                            && string.IsNullOrEmpty(viewModel.QuickCaptureText);
                    }
                    catch (IOException)
                    {
                        return false;
                    }
                });

                await Assert.That(captured).IsTrue();
                await Assert.That(viewModel.QuickCaptureText).IsEmpty();
                await Assert.That(viewModel.Days).HasSingleItem();
                await Assert.That(viewModel.Days[0].Text.Contains("Подготовить прототип", StringComparison.Ordinal)).IsTrue();
                await Assert.That(viewModel.SelectedArea?.Area?.Name).IsEqualTo("Работа");
                await Assert.That(viewModel.SelectedArea?.StableAreaId).IsNull();
                var savedMarkdown = File.ReadAllText(directory.GetDailyPath(today));
                await Assert.That(savedMarkdown).DoesNotContain("unlimotion-area:");
                await Assert.That(savedMarkdown.Split("## Работа", StringSplitOptions.None).Length - 1)
                    .IsEqualTo(1);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_LocalDayBoundaryBeforeLateBoundaryUsesPreviousCalendarDate()
    {
        using var directory = new FeedTempDirectory();
        var localNow = new DateTimeOffset(2026, 8, 24, 23, 15, 0, TimeSpan.FromHours(3));
        using var viewModel = new FeedViewModel(localNowProvider: () => localNow)
        {
            DayBoundary = new TimeSpan(23, 30, 0)
        };
        viewModel.SetNotificationDispatcher(static action => action());
        await viewModel.InitializeVaultAsync(directory.Path);
        viewModel.QuickCaptureText = "Запись до локальной границы дня";

        await viewModel.CaptureAsync();

        var effectiveDate = new DateOnly(2026, 8, 23);
        await Assert.That(viewModel.EffectiveToday).IsEqualTo(effectiveDate);
        await Assert.That(File.Exists(directory.GetDailyPath(effectiveDate))).IsTrue();
        await Assert.That(File.Exists(directory.GetDailyPath(new DateOnly(2026, 8, 24)))).IsFalse();
    }

    [Test]
    public async Task Feed_UnsupportedExternalVaultDisablesOnboardingChooserAndShowsExplanation()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var viewModel = new FeedViewModel(isExternalVaultSupported: false);
            var callbackInvoked = false;
            viewModel.ChooseVaultAsync = () =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            };
            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 640, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                var unsupported = FindControlByAutomationId<TextBlock>(view, "FeedExternalVaultUnsupportedText");
                var chooser = FindControlByAutomationId<Button>(view, "FeedOnboardingChooseVaultButton");

                await Assert.That(unsupported.IsVisible).IsTrue();
                await Assert.That(chooser.IsEnabled).IsFalse();
                InvokeButton(chooser);
                await Assert.That(callbackInvoked).IsFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_FirstConnectSummaryStartsInlineReviewAndKeepsAreaRemapSelected()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var day = new DateOnly(2026, 8, 23);
            using var directory = new FeedTempDirectory();
            directory.WriteDaily(
                day,
                "## Работа <!-- unlimotion-area:work -->\n- [ ] Разобрать кандидата\n\nКонтекст\n\n## Дом <!-- unlimotion-area:home -->\nДомашняя запись\n");
            await SeedAreaCatalogAsync(
                directory.Path,
                new AreaDefinition { Id = "work", Name = "Работа" },
                new AreaDefinition { Id = "home", Name = "Дом" });
            using var viewModel = new FeedViewModel(
                () => new DateOnly(2026, 8, 24),
                reviewDeviceId: "headless-review-device");
            await viewModel.InitializeVaultAsync(directory.Path);

            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 1100, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                var summary = FindControlByAutomationId<Border>(view, "FeedBootstrapSummary");
                var indexed = FindControlByAutomationId<TextBlock>(view, "FeedBootstrapIndexedFilesText");
                var pending = FindControlByAutomationId<TextBlock>(view, "FeedBootstrapPendingCheckboxesText");
                var start = FindControlByAutomationId<Button>(view, "FeedStartReviewButton");

                await Assert.That(summary.IsVisible).IsTrue();
                await Assert.That(indexed.Text).IsEqualTo("1");
                await Assert.That(pending.Text).IsEqualTo("1");
                await Assert.That(viewModel.PendingReviewBlocks).IsEqualTo(1);

                InvokeButton(start);
                await Assert.That(WaitFor(() => viewModel.CurrentReview is not null && !viewModel.IsBusy)).IsTrue();
                RunLayoutJobs();

                var panel = FindControlByAutomationId<Border>(view, "FeedReviewPanel");
                var selectedSurface = FindControlByAutomationId<Control>(view, "FeedReviewInlineAnchorText");
                var expandDown = FindControlByAutomationId<Button>(view, "FeedReviewExpandDownButton");
                await Assert.That(panel.IsVisible).IsTrue();
                await Assert.That(AutomationProperties.GetName(selectedSurface)).Contains("Разобрать кандидата");
                await Assert.That(selectedSurface.GetVisualAncestors()
                    .OfType<MarkdownBlockLivePreviewEditor>()
                    .Any()).IsTrue();
                await Assert.That(viewModel.SelectedDay?.IsReviewTarget).IsTrue();
                await Assert.That(viewModel.SelectedDay?.MarkdownEditor.Blocks
                    .Single(block => block.IsReviewAnchor)
                    .IsReviewHighlighted).IsTrue();

                InvokeButton(expandDown);
                await Assert.That(viewModel.CurrentReview!.SelectedBlockCount).IsEqualTo(2);
                var shrinkDown = FindControlByAutomationId<Button>(view, "FeedReviewShrinkDownButton");
                InvokeButton(shrinkDown);
                await Assert.That(viewModel.CurrentReview.SelectedBlockCount).IsEqualTo(1);

                var home = viewModel.Areas.Single(area => area.Identity == "home");
                var areaPicker = FindControlByAutomationId<ComboBox>(view, "FeedReviewAreaPicker");
                areaPicker.SelectedItem = home;
                RunLayoutJobs();
                var assign = FindControlByAutomationId<Button>(view, "FeedReviewAssignAreaButton");
                InvokeButton(assign);

                var remapped = WaitFor(() =>
                {
                    if (viewModel.IsBusy)
                    {
                        return false;
                    }

                    var raw = File.ReadAllText(directory.GetDailyPath(day));
                    var block = new MarkdownDocumentParser().Parse(raw).Blocks
                        .Single(candidate => candidate.Kind == MarkdownBlockKind.TaskListItem);
                    return block.AreaId == "home" && viewModel.CurrentReview is not null;
                });
                await Assert.That(remapped).IsTrue();
                await Assert.That(viewModel.ReviewTaskAreas.Single(area => area.Area.Identity == "home").IsSelected).IsTrue();
                await Assert.That(viewModel.ReviewTaskAreas.Single(area => area.Area.Identity == "work").IsSelected).IsFalse();
                await Assert.That(viewModel.HasError).IsFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_StartupRecoveryWritesCausalDecisionBeforeClearingTaskJournal()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var day = new DateOnly(2026, 8, 24);
            const string vaultId = "vault-recovery";
            const string operationId = "task-recovery";
            const string taskId = "feed-task-recovery";
            const string reviewDeviceId = "recovery-device";
            const string sourcePath = "Ежедневные/2026-08-24.md";
            const string originalText = "## Работа\n- [ ] Подготовить отчёт\n";
            const string completedText = "## Работа\n[Подготовить отчёт](unlimotion://task/feed-task-recovery)\n";

            using var directory = new FeedTempDirectory();
            directory.WriteDaily(day, completedText);
            var vault = new FileNoteVault(directory.Path);
            await vault.CreateAsync(
                VaultIdentityService.ManifestPath,
                "{\"schemaVersion\":1,\"vaultId\":\"vault-recovery\"}\n");
            var reviewOwner = new FeedReviewSessionCoordinator(
                vaultId,
                reviewDeviceId,
                new PortableReviewEventStore(vault),
                new ReviewStateStore());
            await reviewOwner.InitializeAsync();
            var reviewSessionId = await reviewOwner.OpenOrResumeAsync();

            var parser = new MarkdownDocumentParser();
            var original = parser.Parse(originalText);
            var originalBlock = original.Blocks.Single(static block => block.Kind == MarkdownBlockKind.TaskListItem);
            var inputLocators = FeedReviewQueue.CoveredLocators(
                sourcePath,
                original,
                new MarkdownBlockSelection(originalBlock.Index, 1));
            var completed = parser.Parse(completedText);
            var completedBlock = completed.Blocks.Single(static block => block.IsContent);
            var outputLocators = FeedReviewQueue.CoveredLocators(
                sourcePath,
                completed,
                new MarkdownBlockSelection(completedBlock.Index, 1));
            var currentSource = await vault.ReadAsync(sourcePath)
                ?? throw new InvalidOperationException("The recovery fixture daily note is missing.");
            var taskJournal = new InMemoryFeedTaskConversionJournal();
            await taskJournal.SaveAsync(new FeedTaskConversionRecord(
                2,
                vaultId,
                operationId,
                FeedTaskConversionState.Completed,
                sourcePath,
                "revision-before-conversion",
                taskId,
                currentSource.Revision,
                DateTimeOffset.UtcNow,
                new FeedTaskConversionRecoveryDescriptor(
                    operationId,
                    new MarkdownBlockSelection(originalBlock.Index, 1),
                    FeedOperationHash.Compute(originalBlock.Raw),
                    FeedOperationHash.Compute(completedText),
                    "Подготовить отчёт",
                    string.Empty,
                    false,
                    [],
                    reviewSessionId,
                    inputLocators,
                    outputLocators),
                ReviewApplied: false));

            using var viewModel = new FeedViewModel(
                () => day,
                taskJournalFactory: _ => taskJournal,
                operationJournalFactory: _ => new InMemoryFeedOperationJournal(),
                reviewDeviceId: reviewDeviceId);
            await viewModel.InitializeVaultAsync(directory.Path);

            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                await Assert.That(viewModel.IsVaultInitialized).IsTrue();
                await Assert.That(viewModel.HasError).IsFalse();
                await Assert.That(await taskJournal.ListPendingAsync(vaultId)).IsEmpty();
                var persistedReview = await new PortableReviewEventStore(vault).LoadAllAsync();
                await Assert.That(persistedReview.Decisions.Any(value =>
                    value.OperationId == operationId
                    && value.Decision == ReviewDecision.Converted
                    && value.Input.SemanticKey == inputLocators[0].SemanticKey)).IsTrue();
                await Assert.That(persistedReview.Decisions.Any(value =>
                    value.OperationId == operationId
                    && value.Decision == ReviewDecision.Converted
                    && value.Input.SemanticKey == outputLocators[0].SemanticKey)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_DisposeDuringInitialization_CancelsSessionWithoutStaleMutation()
    {
        var vault = new BlockingNoteVault();
        var viewModel = new FeedViewModel(vaultFactory: _ => vault);
        var initializeTask = viewModel.InitializeVaultAsync("ignored");

        await vault.ListStarted.Task;
        viewModel.Dispose();
        vault.ReleaseList.TrySetResult();
        await initializeTask;

        await Assert.That(viewModel.IsVaultInitialized).IsFalse();
        await Assert.That(viewModel.Days).IsEmpty();
    }

    [Test]
    public async Task Feed_ReviewIncludesPendingCheckboxOutsideInitialChronologyPage()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            var today = new DateOnly(2026, 8, 24);
            var pendingDate = today.AddDays(-16);
            for (var offset = 0; offset < 20; offset++)
            {
                var body = offset == 16
                    ? "- [ ] Кандидат за пределами первой страницы"
                    : $"Обычная запись {offset}";
                directory.WriteDaily(
                    today.AddDays(-offset),
                    $"## Работа <!-- unlimotion-area:work -->\n{body}\n");
            }

            using var viewModel = new FeedViewModel(
                () => today,
                reviewDeviceId: "headless-review-all-days");
            await viewModel.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                await Assert.That(viewModel.Days.Count).IsEqualTo(14);
                await Assert.That(viewModel.Days.Any(day => day.Date == pendingDate)).IsFalse();
                await Assert.That(viewModel.PendingReviewBlocks).IsEqualTo(1);
                await Assert.That(viewModel.PendingReviewDays).IsEqualTo(1);

                InvokeButton(FindControlByAutomationId<Button>(view, "FeedStartReviewButton"));

                await Assert.That(WaitFor(
                    () => !viewModel.IsBusy
                        && viewModel.CurrentReview?.Date == pendingDate
                        && viewModel.SelectedDay?.Date == pendingDate,
                    timeoutMilliseconds: 10000)).IsTrue();
                await Assert.That(viewModel.Days.Any(day => day.Date == pendingDate)).IsTrue();
                await Assert.That(viewModel.SelectedDay!.IsReviewTarget).IsTrue();
                await Assert.That(viewModel.SelectedDay.MarkdownEditor.Blocks
                    .Any(block => block.IsReviewAnchor && block.IsReviewHighlighted)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_DayCollapseStateSurvivesChronologyRefresh()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            var today = new DateOnly(2026, 8, 24);
            directory.WriteDaily(today, "## Работа <!-- unlimotion-area:work -->\nЗапись дня\n");

            using var viewModel = new FeedViewModel(() => today);
            await viewModel.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 700, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                var original = viewModel.Days.Single();
                var toggle = FindControlByAutomationId<ToggleButton>(view, original.CollapseAutomationId);
                toggle.IsChecked = true;
                RunLayoutJobs();

                await Assert.That(original.IsCollapsed).IsTrue();
                await viewModel.RefreshAsync();
                RunLayoutJobs();

                var refreshed = viewModel.Days.Single();
                await Assert.That(refreshed.IsCollapsed).IsTrue();
                await Assert.That(FindControlByAutomationId<ToggleButton>(view, refreshed.CollapseAutomationId)
                    .IsChecked).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Feed_MoveToTodayDefersEveryDestinationBlockFromCurrentSession()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new FeedTempDirectory();
            var today = new DateOnly(2026, 8, 24);
            var sourceDate = today.AddDays(-1);
            directory.WriteDaily(
                sourceDate,
                "## Работа <!-- unlimotion-area:work -->\n- [ ] Перенести целиком\n");
            var journal = new InMemoryFeedOperationJournal();

            using var viewModel = new FeedViewModel(
                () => today,
                operationJournalFactory: _ => journal,
                reviewDeviceId: "headless-review-move-all",
                revisionStoreFactory: _ => new BoundedRevisionStore(
                    System.IO.Path.Combine(directory.Path, ".test-revisions")));
            await viewModel.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 900, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                InvokeButton(FindControlByAutomationId<Button>(view, "FeedStartReviewButton"));
                await Assert.That(WaitFor(
                    () => !viewModel.IsBusy && viewModel.CurrentReview?.Date == sourceDate,
                    timeoutMilliseconds: 8000)).IsTrue();

                InvokeButton(FindControlByAutomationId<Button>(view, "FeedReviewMoveTodayButton"));
                await Assert.That(WaitFor(
                    () => !viewModel.IsBusy && File.Exists(directory.GetDailyPath(today)),
                    timeoutMilliseconds: 10000)).IsTrue();

                await Assert.That(viewModel.ErrorMessage).IsNull();
                var destinationText = File.ReadAllText(directory.GetDailyPath(today));
                var operationId = destinationText
                    .Split("^unlimotion-move-", StringSplitOptions.None)[1]
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
                var identity = await new VaultIdentityService(new FileNoteVault(directory.Path))
                    .GetOrCreateAsync();
                var completedOperation = await journal.LoadAsync(
                    identity.VaultId,
                    operationId);
                await Assert.That(completedOperation).IsNotNull();
                await Assert.That(completedOperation!.ReviewApplied).IsTrue();
                await Assert.That(completedOperation.RecoveryDescriptor?.DestinationOutputLocators?.Count)
                    .IsEqualTo(2);

                var persisted = await new PortableReviewEventStore(
                        new FileNoteVault(directory.Path))
                    .LoadAllAsync();
                var deferredDestination = persisted.Decisions
                    .Where(reviewEvent => reviewEvent.Decision == ReviewDecision.Deferred
                        && reviewEvent.OperationId?.EndsWith("-destination", StringComparison.Ordinal) == true)
                    .ToArray();

                await Assert.That(deferredDestination.Length).IsEqualTo(2);
                await Assert.That(deferredDestination
                    .Select(static reviewEvent => reviewEvent.Input.SemanticKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count()).IsEqualTo(2);
                await Assert.That(deferredDestination.Any(reviewEvent =>
                    reviewEvent.Input.BlockKind == MarkdownBlockKind.TaskListItem)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = FindOptionalControlByAutomationId<T>(root, automationId);

        return control ?? throw new InvalidOperationException(
            $"Control with AutomationId '{automationId}' was not found.");
    }

    private static T? FindOptionalControlByAutomationId<T>(Control root, string automationId)
        where T : Control => root.GetVisualDescendants()
        .OfType<T>()
        .FirstOrDefault(candidate => string.Equals(
            AutomationProperties.GetAutomationId(candidate),
            automationId,
            StringComparison.Ordinal));

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 4000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, int timeoutMilliseconds = 4000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        Dispatcher.UIThread.RunJobs();
        return predicate();
    }

    private static void InvokeButton(Button button)
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

    private static async Task<MenuFlyout> OpenStatusFlyoutAsync(global::Unlimotion.TaskStatusPicker statusPicker)
    {
        var point = new Point(statusPicker.Bounds.Width / 2, statusPicker.Bounds.Height / 2);
        var pointer = new Pointer(1, PointerType.Mouse, true);
        statusPicker.RaiseEvent(new PointerPressedEventArgs(
            statusPicker,
            pointer,
            statusPicker,
            point,
            0,
            new PointerPointProperties(
                RawInputModifiers.LeftMouseButton,
                PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            1));
        Dispatcher.UIThread.RunJobs();
        statusPicker.RaiseEvent(new PointerReleasedEventArgs(
            statusPicker,
            pointer,
            statusPicker,
            point,
            0,
            new PointerPointProperties(
                RawInputModifiers.None,
                PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));

        var opened = await WaitForAsync(() => statusPicker.Flyout is MenuFlyout { IsOpen: true });
        return opened && statusPicker.Flyout is MenuFlyout flyout
            ? flyout
            : throw new InvalidOperationException("The feed task status flyout was not opened.");
    }

    private static void RunLayoutJobs()
    {
        for (var index = 0; index < 20; index++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task SeedAreaCatalogAsync(string rootPath, params AreaDefinition[] areas)
    {
        var store = new AreaCatalogStore(new FileNoteVault(rootPath));
        _ = await store.SaveAsync(new AreaCatalog { Areas = [.. areas] }, expectedRevision: null);
    }

    private sealed class RecordingFeedTaskTarget : IFeedTaskCreationTarget
    {
        public List<FeedTaskDraft> Tasks { get; } = [];

        public Task<FeedCreatedTask> CreateOrGetAsync(
            FeedTaskDraft draft,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Tasks.Add(draft);
            return Task.FromResult(new FeedCreatedTask(draft.TaskId, draft.Title));
        }
    }

    private sealed class FeedTempDirectory : IDisposable
    {
        public FeedTempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "unlimotion-feed-ui-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void WriteDaily(DateOnly date, string text)
        {
            var path = GetDailyPath(date);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void WriteNote(string relativePath, string text)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public string GetDailyPath(DateOnly date) => System.IO.Path.Combine(
            Path,
            "Ежедневные",
            $"{date:yyyy-MM-dd}.md");

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class BlockingNoteVault : INoteVault
    {
        public TaskCompletionSource ListStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseList { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string RootPath => "blocking-vault";

        public Task<VaultDocument?> ReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<VaultDocument?>(string.Equals(
                    relativePath,
                    VaultIdentityService.ManifestPath,
                    StringComparison.Ordinal)
                ? new VaultDocument(
                    relativePath,
                    "{\"schemaVersion\":1,\"vaultId\":\"blocking-vault\"}\n",
                    new string('a', 64),
                    false,
                    "\n")
                : null);

        public Task<VaultWriteResult> WriteAsync(
            string relativePath,
            string text,
            string? expectedRevision,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<VaultWriteResult> CreateAsync(
            string relativePath,
            string text,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<IReadOnlyList<string>> ListMarkdownFilesAsync(CancellationToken cancellationToken = default)
        {
            ListStarted.TrySetResult();
            await ReleaseList.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return Array.Empty<string>();
        }

        public Task<IReadOnlyList<string>> ListFilesAsync(
            string relativeDirectory,
            string searchPattern,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public string ResolveSafePath(string relativePath) => relativePath;
    }
}
