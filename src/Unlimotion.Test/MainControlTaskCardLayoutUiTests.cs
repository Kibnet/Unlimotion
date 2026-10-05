using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlTaskCardLayoutUiTests
{
    private const string BorderlessTextBoxChromeClass = "BorderlessTextBoxChrome";

    private static readonly string[] SectionAutomationIds =
    [
        "CurrentTaskCard",
        "CurrentTaskHeader",
        "CurrentTaskCommandBar",
        "CurrentTaskDescriptionSection",
        "CurrentTaskPlanningSection",
        "CurrentTaskRepeaterSection",
        "CurrentTaskRelationsSection",
        "CurrentTaskCompletionCriteriaSection",
        "CurrentTaskStatusHistorySection"
    ];

    private static readonly string[] KeyControlAutomationIds =
    [
        "CurrentTaskStatusButton",
        "CurrentTaskTitleTextBox",
        "CurrentTaskWantedCheckBox",
        "CurrentTaskImportanceInput",
        "CurrentTaskIdTextBlock",
        "CurrentTaskDescriptionTextBox",
        "CurrentTaskPlannedBeginPicker",
        "CurrentTaskSetBeginButton",
        "CurrentTaskPlannedDurationTextBox",
        "CurrentTaskSetDurationButton",
        "CurrentTaskPlannedEndPicker",
        "CurrentTaskSetEndButton",
        "CurrentTaskRepeaterSelector",
        "AddCompletionCriterionButton",
        "StatusHistoryExpander"
    ];

    private static readonly string[] PlanningControlAutomationIds =
    [
        "CurrentTaskPlannedBeginPicker",
        "CurrentTaskSetBeginButton",
        "CurrentTaskPlannedDurationTextBox",
        "CurrentTaskSetDurationButton",
        "CurrentTaskPlannedEndPicker",
        "CurrentTaskSetEndButton"
    ];

    [Test]
    public async Task CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 1400, 900);
                window = createdWindow;

                foreach (var automationId in SectionAutomationIds.Concat(KeyControlAutomationIds))
                {
                    var control = FindControlByAutomationId<Control>(view, automationId);
                    AssertVisibleAndArranged(control, automationId);
                }

                var detailsPanelFrame = FindControlByAutomationId<Border>(view, "CurrentTaskDetailsPanelFrame");
                var card = FindControlByAutomationId<Border>(view, "CurrentTaskCard");
                var header = FindControlByAutomationId<Control>(card, "CurrentTaskHeader");
                var createMenuButton = FindControlByAutomationId<DropDownButton>(view, "GlobalTaskCreateMenuButton");
                var actionsMenuButton = FindControlByAutomationId<DropDownButton>(view, "CurrentTaskActionsMenuButton");
                var titleTextBox = FindControlByAutomationId<TextBox>(view, "CurrentTaskTitleTextBox");
                var descriptionTextBox = FindControlByAutomationId<TextBox>(view, "CurrentTaskDescriptionTextBox");
                var setBeginButton = FindControlByAutomationId<DropDownButton>(view, "CurrentTaskSetBeginButton");
                var setDurationButton = FindControlByAutomationId<DropDownButton>(view, "CurrentTaskSetDurationButton");
                var setEndButton = FindControlByAutomationId<DropDownButton>(view, "CurrentTaskSetEndButton");

                AssertTaskDetailsPanelFrameUsesVisibleBorder(detailsPanelFrame);
                AssertTaskCardIsContentContainer(card);
                AssertHasClass(createMenuButton, "TaskCreateMenuButton");
                AssertIconOnlyDropDownButton(createMenuButton, "➕", 42);
                AssertCreateMenuContainsTaskCommands(createMenuButton);
                AssertHasClass(actionsMenuButton, "TaskActionsMenuButton");
                AssertIconOnlyDropDownButton(actionsMenuButton, "⚙", 36);
                AssertActionsMenuContainsTaskCommands(actionsMenuButton);
                AssertHasClass(titleTextBox, "CurrentTaskTitleEditor");
                AssertHasClass(titleTextBox, BorderlessTextBoxChromeClass);
                AssertBorderlessTextBoxChrome(titleTextBox, "Current task title");
                AssertTaskHeaderAlignment(header);
                AssertHasClass(descriptionTextBox, "TaskDescriptionEditor");
                AssertHasClass(setBeginButton, "TaskPlanningQuickAction");
                AssertHasClass(setDurationButton, "TaskPlanningQuickAction");
                AssertHasClass(setEndButton, "TaskPlanningQuickAction");
                AssertIconOnlyDropDownButton(setBeginButton, "📅", 40);
                AssertIconOnlyDropDownButton(setDurationButton, "⏱", 40);
                AssertIconOnlyDropDownButton(setEndButton, "🏁", 40);

                AssertTaskActionsMenuSitsAfterIdBelowTitle(view);
                AssertDesktopPlanningGroupsStayCompactRow(view);
                AssertDesktopRepeaterControlsStayCompact(view);
                AssertStatusHistoryLivesAtTaskCardBottomAndExpandsDown(view);

                var relations = FindControlByAutomationId<Control>(view, "CurrentTaskRelationsSection");
                var parentsAddButton = FindControlByAutomationId<Button>(relations, "CurrentTaskParentsRelationAddButton");
                var parentsTree = FindControlByAutomationId<TreeView>(relations, "CurrentItemParentsTree");

                await Assert.That(IsVisibleAndArranged(parentsAddButton)).IsTrue();
                AssertHasClass(parentsAddButton, "RelationAddButton");
                AssertIconOnlyButton(parentsAddButton, "＋", 26);
                await Assert.That(parentsTree).IsNotNull();
                var relationTrees = relations.GetVisualDescendants().OfType<TreeView>()
                    .Where(tree => tree.Classes.Contains("RelationTaskTree")).ToArray();
                await Assert.That(relationTrees.Any(tree => tree.ItemCount == 0)).IsTrue();
                foreach (var tree in relationTrees)
                    await Assert.That(tree.IsVisible).IsEqualTo(tree.ItemCount > 0);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_ParentEmojiTrail_ShowsAncestorsBeforeTaskId()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            var expectedTrail = string.Empty;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1400,
                    900,
                    MainWindowViewModelFixture.SubTask22Id,
                    task =>
                    {
                        var parents = task.ParentsTasks.OrderBy(parent => parent.Id).ToArray();
                        parents[0].Title = "🧭 Alpha parent";
                        parents[1].Title = "🛠 Beta parent";
                        expectedTrail = string.Concat(task.GetAllParents().Select(parent => parent.Emoji));
                    });
                window = createdWindow;

                var title = FindControlByAutomationId<Control>(view, "CurrentTaskTitleTextBox");
                var id = FindControlByAutomationId<Control>(view, "CurrentTaskIdTextBlock");
                var trail = FindControlByAutomationId<EmojiTextBlock>(view, "CurrentTaskParentEmojiTrail");
                var actions = FindControlByAutomationId<Control>(view, "CurrentTaskActionsMenuButton");

                await Assert.That(expectedTrail).IsEqualTo("🧭🛠");
                await Assert.That(trail.EmojiText).IsEqualTo(expectedTrail);
                await Assert.That(IsVisibleAndArranged(trail)).IsTrue();
                await Assert.That(GetTopEdge(view, trail)).IsGreaterThanOrEqualTo(GetBottomEdge(view, title) - 1);
                await Assert.That(GetRightEdge(view, trail)).IsLessThan(GetLeftEdge(view, id));
                await Assert.That(GetLeftEdge(view, actions)).IsGreaterThan(GetRightEdge(view, id));
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_ParentEmojiTrail_RefreshesWhenAncestorTitleChangesBeforeSave()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1400,
                    900,
                    MainWindowViewModelFixture.SubTask22Id);
                window = createdWindow;

                var task = fixture.MainWindowViewModelTest.CurrentTaskItem!;
                var ancestor = task.ParentsTasks.OrderBy(parent => parent.Id).First();
                var trail = FindControlByAutomationId<EmojiTextBlock>(view, "CurrentTaskParentEmojiTrail");

                ancestor.Title = "🧭 Renamed ancestor";
                RunLayoutJobs();

                var expectedTrail = string.Concat(task.GetAllParents().Select(parent => parent.Emoji));
                await Assert.That(trail.EmojiText).IsEqualTo(expectedTrail);
                await Assert.That(trail.EmojiText).Contains("🧭");
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public Task CurrentTaskCard_ParentEmojiTrail_TitleInputUpdatesChildrenAndGrandchildrenBeforeSave() =>
        AssertTitleInputUpdatesDescendantsAsync("🧭", "🫶", string.Empty);

    [Test]
    public Task CurrentTaskCard_ExistingTaskWizardToJellyfish_UpdatesChildrenAndGrandchildrenBeforeSave() =>
        AssertTitleInputUpdatesDescendantsAsync("🧙‍♂️", "🪼", "jellyfish-");

    private static async Task AssertTitleInputUpdatesDescendantsAsync(
        string originalEmoji, string replacementEmoji, string capturePrefix)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            await using var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                foreach (var (id, initialTitle) in new[]
                {
                    (MainWindowViewModelFixture.RootTask2Id, $"{originalEmoji} Проект"),
                    (MainWindowViewModelFixture.RootTask3Id, "🛠 Вторая ветка")
                })
                {
                    var path = Path.Combine(fixture.DefaultTasksFolderPath, id);
                    var json = JObject.Parse(File.ReadAllText(path));
                    json["Title"] = initialTitle;
                    if (id == MainWindowViewModelFixture.RootTask2Id)
                        json[nameof(TaskItem.PlannedBeginDateTime)] = new JValue(DateTime.Today);
                    File.WriteAllText(path, json.ToString());
                }
                var childPath = Path.Combine(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask22Id);
                var childJson = JObject.Parse(File.ReadAllText(childPath));
                var grandchildId = Guid.NewGuid().ToString();
                childJson[nameof(TaskItem.ContainsTasks)] = new JArray(grandchildId);
                File.WriteAllText(childPath, childJson.ToString());
                File.WriteAllText(Path.Combine(fixture.DefaultTasksFolderPath, grandchildId),
                    JsonConvert.SerializeObject(new TaskItem
                    {
                        Id = grandchildId,
                        Title = "Внук проекта",
                        Status = DomainTaskStatus.Prepared,
                        ParentTasks = [MainWindowViewModelFixture.SubTask22Id]
                    }));

                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 1400, 900);
                window = createdWindow;
                var vm = fixture.MainWindowViewModelTest;
                await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(task => task.SealPendingSaves()));
                var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var diskBefore = File.ReadAllText(Path.Combine(fixture.DefaultTasksFolderPath, parent.Id));
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.SubTask22Id);
                RunLayoutJobs();
                var beforeTrail = FindControlByAutomationId<EmojiTextBlock>(view, "CurrentTaskParentEmojiTrail");
                await Assert.That(IsVisibleAndArranged(beforeTrail)).IsTrue();
                await Assert.That(beforeTrail.EmojiText).Contains(originalEmoji);
                await Assert.That(beforeTrail.EmojiText).Contains("🛠");
                SaveEmojiTitleFrame(window, capturePrefix + "card-before");

                var changeIndex = 0;
                foreach (var emoji in new[] { replacementEmoji, "🐦‍🔥", "", originalEmoji })
                {
                    changeIndex++;
                    parent = TestHelpers.SetCurrentTask(vm, parent.Id);
                    RunLayoutJobs();
                    var titleInput = FindControlByAutomationId<TextBox>(view, "CurrentTaskTitleTextBox");
                    var inputReady = await TestHelpers.WaitUntilAsync(() =>
                    {
                        RunLayoutJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        RunLayoutJobs();
                        return IsVisibleAndArranged(titleInput) && ReferenceEquals(titleInput.DataContext, parent) &&
                            titleInput.Text == parent.Title;
                    }, TimeSpan.FromSeconds(2));
                    if (!inputReady)
                        Console.Error.WriteLine("Emoji card input readiness: " + JsonConvert.SerializeObject(new
                        {
                            parent.Id, parent.Title, parent.Emoji, InputText = titleInput.Text,
                            InputIsParent = ReferenceEquals(titleInput.DataContext, parent),
                            CacheIsParent = ReferenceEquals(TestHelpers.GetTask(vm, parent.Id), parent)
                        }));
                    await Assert.That(inputReady).IsTrue();
                    await Assert.That(titleInput.Focus()).IsTrue();
                    titleInput.SelectAll();
                    window.KeyTextInput($"{emoji} Проект");
                    RunLayoutJobs();
                    await Assert.That(parent.Title).IsEqualTo($"{emoji} Проект");
                    await Assert.That(parent.Emoji).IsEqualTo(emoji);

                    foreach (var descendantId in new[] { MainWindowViewModelFixture.SubTask22Id, grandchildId })
                    {
                        var descendant = TestHelpers.SetCurrentTask(vm, descendantId);
                        RunLayoutJobs();
                        var trail = FindControlByAutomationId<EmojiTextBlock>(view, "CurrentTaskParentEmojiTrail");
                        var expectedTitle = $"{emoji} Проект";
                        var trailReady = await TestHelpers.WaitUntilAsync(() =>
                        {
                            RunLayoutJobs();
                            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                            RunLayoutJobs();
                            var ancestors = descendant.GetAllParents().ToArray();
                            var editedAncestor = ancestors.Single(ancestor => ancestor.Id == parent.Id);
                            var expectedTrail = string.Concat(ancestors.Select(ancestor => ancestor.Emoji));
                            return ReferenceEquals(trail.DataContext, descendant) &&
                                parent.Title == expectedTitle && parent.Emoji == emoji &&
                                editedAncestor.Title == expectedTitle && editedAncestor.Emoji == emoji &&
                                IsVisibleAndArranged(trail) && trail.EmojiText == expectedTrail &&
                                descendant.ParentEmojiTrail == expectedTrail && descendant.GetAllEmoji == expectedTrail;
                        }, TimeSpan.FromSeconds(2));
                        if (!trailReady)
                            Console.Error.WriteLine("Emoji card trail readiness: " + JsonConvert.SerializeObject(new
                            {
                                ExpectedTitle = expectedTitle, parent.Id, parent.Title, parent.Emoji,
                                CacheIsParent = ReferenceEquals(TestHelpers.GetTask(vm, parent.Id), parent),
                                CacheTitle = TestHelpers.GetTask(vm, parent.Id).Title,
                                descendant.ParentEmojiTrail, descendant.GetAllEmoji, TrailText = trail.EmojiText,
                                TrailIsDescendant = ReferenceEquals(trail.DataContext, descendant),
                                Ancestors = descendant.GetAllParents().Select(ancestor => new
                                {
                                    ancestor.Id, ancestor.Title, ancestor.Emoji,
                                    IsEditedParent = ReferenceEquals(ancestor, parent)
                                }).ToArray()
                            }));
                        await Assert.That(trailReady).IsTrue();
                        SaveEmojiTitleFrame(window, capturePrefix + $"{changeIndex}-" + (descendantId == grandchildId ? "grandchild-after" : "card-after"));
                        await Assert.That(IsVisibleAndArranged(trail)).IsTrue();
                        await Assert.That(trail.EmojiText).Contains("🛠");
                        if (emoji.Length > 0)
                        {
                            await Assert.That(trail.EmojiText).Contains(emoji);
                            await Assert.That(descendant.GetAllEmoji).Contains(emoji);
                        }
                        else
                        {
                            await Assert.That(trail.EmojiText).IsEqualTo("🛠");
                        }
                        await Assert.That(trail.EmojiText)
                            .IsEqualTo(string.Concat(descendant.GetAllParents().Select(ancestor => ancestor.Emoji)));
                    }
                    await Assert.That(File.ReadAllText(Path.Combine(fixture.DefaultTasksFolderPath, parent.Id)))
                        .IsEqualTo(diskBefore);
                }
            }
            finally
            {
                CloseWindow(window);
            }
        }, CancellationToken.None);
    }

    private static void SaveEmojiTitleFrame(Window window, string state)
    {
        if (Environment.GetEnvironmentVariable("UNLIMOTION_TEST_CAPTURE_FRAMES") != "1") return;
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_TEST_TRACE_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        using var frame = window.CaptureRenderedFrame();
        if (frame == null) throw new InvalidOperationException("Emoji title regression frame was unavailable.");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, $"emoji-title-{state}.png"));
    }

    [Test]
    public async Task CurrentTaskCard_ParentEmojiTrail_HidesForRootTask()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1400,
                    900,
                    MainWindowViewModelFixture.RootTask1Id,
                    task =>
                    {
                        task.Title = "🧭 Root task";
                        task.RefreshComputedFields();
                    });
                window = createdWindow;

                var trail = FindControlByAutomationId<EmojiTextBlock>(view, "CurrentTaskParentEmojiTrail");

                await Assert.That(trail.IsVisible).IsFalse();
                await Assert.That(trail.EmojiText).IsEmpty();
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_PlanningDatePickers_UseDurationFieldPadding()
    {
        using var phases = new TestScenarioPhases(nameof(CurrentTaskCard_PlanningDatePickers_UseDurationFieldPadding));
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 1400, 900);
                window = createdWindow;
                phases.Next("body");

                var beginPicker = FindControlByAutomationId<CalendarDatePicker>(
                    view,
                    "CurrentTaskPlannedBeginPicker");
                var durationTextBox = FindControlByAutomationId<TextBox>(
                    view,
                    "CurrentTaskPlannedDurationTextBox");
                var endPicker = FindControlByAutomationId<CalendarDatePicker>(
                    view,
                    "CurrentTaskPlannedEndPicker");

                await Assert.That(beginPicker.Padding).IsEqualTo(durationTextBox.Padding);
                await Assert.That(endPicker.Padding).IsEqualTo(durationTextBox.Padding);
                await Assert.That(beginPicker.Padding.Left).IsGreaterThan(0);
                await Assert.That(endPicker.Padding.Right).IsGreaterThan(0);
            }
            finally
            {
                phases.Next("cleanup");
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_DarkTheme_UsesThemeAwareAccentButtonChrome()
    {
        using var phases = new TestScenarioPhases(nameof(CurrentTaskCard_DarkTheme_UsesThemeAwareAccentButtonChrome));
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var app = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");
            var previousTheme = app.RequestedThemeVariant;

            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 1400, 900);
                window = createdWindow;
                phases.Next("body");
                app.RequestedThemeVariant = ThemeVariant.Dark;
                ArrangeMainControlForTest(createdWindow, view, createdWindow.Width, createdWindow.Height);

                var detailsPanelFrame = FindControlByAutomationId<Border>(view, "CurrentTaskDetailsPanelFrame");
                var card = FindControlByAutomationId<Border>(view, "CurrentTaskCard");
                Control[] accentOutlineButtons =
                [
                    FindControlByAutomationId<DropDownButton>(view, "CurrentTaskActionsMenuButton"),
                    FindControlByAutomationId<DropDownButton>(view, "CurrentTaskSetBeginButton"),
                    FindControlByAutomationId<DropDownButton>(view, "CurrentTaskSetDurationButton"),
                    FindControlByAutomationId<DropDownButton>(view, "CurrentTaskSetEndButton"),
                    FindControlByAutomationId<Button>(view, "CurrentTaskParentsRelationAddButton"),
                    FindControlByAutomationId<Button>(view, "CurrentTaskBlockingRelationAddButton"),
                    FindControlByAutomationId<Button>(view, "CurrentTaskContainingRelationAddButton"),
                    FindControlByAutomationId<Button>(view, "CurrentTaskBlockedRelationAddButton"),
                    FindControlByAutomationId<DropDownButton>(view, "GlobalTaskCreateMenuButton")
                ];

                AssertTaskDetailsPanelFrameUsesVisibleBorder(detailsPanelFrame);
                AssertTaskCardIsContentContainer(card);
                foreach (var button in accentOutlineButtons)
                {
                    AssertDoesNotUseLightThemeAccentBackground(button);
                    AssertHasClass(button, "TaskAccentOutlineButton");
                }
            }
            finally
            {
                phases.Next("cleanup");
                try
                {
                    CloseWindow(window);
                    await fixture.CleanTasksAsync();
                }
                finally
                {
                    app.RequestedThemeVariant = previousTheme;
                }
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_DesktopRepeaterLayout_UsesCompactControls()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        try
        {
            await session.DispatchAsync(async () =>
            {
                ResetTaskCardLayoutSharedState();
                var fixture = new MainWindowViewModelFixture();
                Window? window = null;

                try
                {
                    var (view, createdWindow) = await CreateArrangedMainControlAsync(
                        fixture,
                        1400,
                        900,
                        MainWindowViewModelFixture.RepeateTask9Id,
                        task =>
                        {
                            task.Repeater!.Type = RepeaterType.Weekly;
                            task.Repeater.WorkDays = true;
                        });
                    window = createdWindow;

                    AssertDesktopRepeaterControlsStayCompact(view, requireWeekdayToggles: true);
                }
                finally
                {
                    CloseWindow(window);
                    await fixture.CleanTasksAsync();
                }
            }, CancellationToken.None);
        }
        finally
        {
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }
    }

    [Test]
    public async Task CurrentTaskCard_IntermediateDesktopWidthRepeaterLayout_DoesNotOverlap()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1032,
                    900,
                    MainWindowViewModelFixture.RepeateTask9Id,
                    task =>
                    {
                        task.Repeater!.Type = RepeaterType.Weekly;
                        task.Repeater.WorkDays = true;
                    });
                window = createdWindow;

                AssertDesktopRepeaterControlsStayCompact(view, requireWeekdayToggles: true);
                AssertRepeaterControlsDoNotOverlap(view);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_IntermediateDesktopWidthRepeaterLayout_WithLargeFontDoesNotOverlap()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1032,
                    900,
                    MainWindowViewModelFixture.RepeateTask9Id,
                    task =>
                    {
                        task.Repeater!.Type = RepeaterType.Weekly;
                        task.Repeater.WorkDays = true;
                    },
                    fontSize: 24d);
                window = createdWindow;

                var scrollViewer = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
                var card = FindControlByAutomationId<Control>(view, "CurrentTaskCard");

                AssertRepeaterControlsDoNotOverlap(view);
                AssertNoHorizontalOverflow(scrollViewer, card);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_CompletedTask_DisablesCompletionCriteriaEditing()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1400,
                    900,
                    MainWindowViewModelFixture.RootTask1Id,
                    task =>
                    {
                        task.Status = DomainTaskStatus.Completed;
                        task.CompletionCriteria.Add(new TaskCompletionCriterion
                        {
                            Text = "Проверить результат",
                            IsSatisfied = true
                        });
                    });
                window = createdWindow;
                RunLayoutJobs();

                var section = FindControlByAutomationId<Control>(view, "CurrentTaskCompletionCriteriaSection");
                var addButton = FindControlByAutomationId<Button>(section, "AddCompletionCriterionButton");
                var items = FindControlByAutomationId<ItemsControl>(section, "CompletionCriteriaItems");

                AssertVisibleAndArranged(section, "CurrentTaskCompletionCriteriaSection");
                await Assert.That(addButton.IsEnabled).IsFalse();
                await Assert.That(items.IsEnabled).IsFalse();
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_CompletionCriterionRow_UsesBorderlessCompactEditing()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1400,
                    900,
                    MainWindowViewModelFixture.RootTask1Id,
                    task =>
                    {
                        task.CompletionCriteria.Add(new TaskCompletionCriterion
                        {
                            Text = "Проверить результат",
                            IsSatisfied = true
                        });
                    });
                window = createdWindow;
                RunLayoutJobs();

                var section = FindControlByAutomationId<Control>(view, "CurrentTaskCompletionCriteriaSection");
                var checkBox = FindControlByAutomationId<CheckBox>(section, "CompletionCriterionSatisfiedCheckBox");
                var textBox = FindControlByAutomationId<TextBox>(section, "CompletionCriterionTextBox");
                var removeButton = FindControlByAutomationId<Button>(section, "CompletionCriterionRemoveButton");
                var gapAfterCheckBox = GetLeftEdge(section, textBox) - GetRightEdge(section, checkBox);

                await Assert.That(textBox.Classes.Contains("CompletionCriterionTextBox")).IsTrue();
                AssertHasClass(textBox, BorderlessTextBoxChromeClass);
                AssertBorderlessTextBoxChrome(textBox, "Completion criterion");
                await Assert.That(gapAfterCheckBox).IsLessThanOrEqualTo(6);
                AssertVectorRemoveButton(removeButton, 28);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_LongCompletionCriterion_PhoneWidthWrapsWithoutHorizontalOverflow()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    360,
                    844,
                    MainWindowViewModelFixture.RootTask1Id,
                    task =>
                    {
                        task.CompletionCriteria.Add(new TaskCompletionCriterion
                        {
                            Text =
                                "Review the prepared lesson plan with every stakeholder and confirm that " +
                                "the launch checklist, support notes, and retrospective prompts are all complete.",
                            IsSatisfied = false
                        });
                    });
                window = createdWindow;
                RunLayoutJobs();

                var scrollViewer = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
                var card = FindControlByAutomationId<Control>(view, "CurrentTaskCard");
                var section = FindControlByAutomationId<Control>(view, "CurrentTaskCompletionCriteriaSection");
                var checkBox = FindControlByAutomationId<CheckBox>(section, "CompletionCriterionSatisfiedCheckBox");
                var textBox = FindControlByAutomationId<TextBox>(section, "CompletionCriterionTextBox");
                var removeButton = FindControlByAutomationId<Button>(section, "CompletionCriterionRemoveButton");

                await Assert.That(textBox.TextWrapping).IsEqualTo(TextWrapping.Wrap);
                await Assert.That(textBox.Bounds.Height).IsGreaterThan(44);
                AssertHorizontallyContained(scrollViewer, checkBox);
                AssertHorizontallyContained(scrollViewer, textBox);
                AssertHorizontallyContained(scrollViewer, removeButton);
                AssertNoHorizontalOverflow(scrollViewer, card);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_AddCompletionCriterion_FocusesNewCriterionTextBox()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1400,
                    900,
                    MainWindowViewModelFixture.RootTask1Id);
                window = createdWindow;
                RunLayoutJobs();

                var section = FindControlByAutomationId<Control>(view, "CurrentTaskCompletionCriteriaSection");
                var addButton = FindControlByAutomationId<Button>(section, "AddCompletionCriterionButton");

                await Assert.That(addButton.Command?.CanExecute(addButton.CommandParameter)).IsTrue();
                addButton.Command!.Execute(addButton.CommandParameter);
                RunLayoutJobs();

                TextBox? focusedTextBox = null;
                var focused = await TestHelpers.WaitUntilAsync(
                    () =>
                    {
                        RunLayoutJobs();
                        focusedTextBox = section.GetVisualDescendants()
                            .OfType<TextBox>()
                            .Where(candidate =>
                                string.Equals(
                                    AutomationProperties.GetAutomationId(candidate),
                                    "CompletionCriterionTextBox",
                                    StringComparison.Ordinal))
                            .SingleOrDefault();

                        return focusedTextBox != null && IsFocused(window, focusedTextBox);
                    },
                    TimeSpan.FromSeconds(2));

                await Assert.That(focused).IsTrue();
                await Assert.That(focusedTextBox).IsNotNull();
                await Assert.That(focusedTextBox!.DataContext).IsAssignableTo<TaskCompletionCriterion>();
                await Assert.That(focusedTextBox.CaretIndex).IsEqualTo(focusedTextBox.Text?.Length ?? 0);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_RemoveButtonsUseCenteredVectorIcons()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1400,
                    900,
                    MainWindowViewModelFixture.RootTask1Id,
                    task =>
                    {
                        task.CompletionCriteria.Add(new TaskCompletionCriterion
                        {
                            Text = "Проверить результат",
                            IsSatisfied = false
                        });
                    });
                window = createdWindow;
                RunLayoutJobs();

                var completionCriteriaSection = FindControlByAutomationId<Control>(
                    view,
                    "CurrentTaskCompletionCriteriaSection");
                var completionCriterionRemoveButton = FindControlByAutomationId<Button>(
                    completionCriteriaSection,
                    "CompletionCriterionRemoveButton");
                var relationRemoveButton = view.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Classes.Contains("TaskRowRemoveButton"))
                    .FirstOrDefault(IsVisibleAndArranged)
                    ?? throw new InvalidOperationException("Visible task row remove button was not found.");

                AssertVectorRemoveButton(completionCriterionRemoveButton, 28);
                AssertVectorRemoveButton(relationRemoveButton, 28);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_BackGestureFallback_OpensPaneForSingleVisibleTask()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 390, 844);
                window = createdWindow;
                var vm = fixture.MainWindowViewModelTest;
                var task = vm.CurrentTaskItem!;
                var items = new ObservableCollection<TaskWrapperViewModel>
                {
                    new(null, task, new TaskWrapperActions())
                };
                var splitView = view.GetVisualDescendants().OfType<SplitView>().First();

                vm.CurrentAllTasksItems = new ReadOnlyObservableCollection<TaskWrapperViewModel>(items);
                vm.CurrentAllTasksItem = null;
                vm.CurrentTaskItem = null;
                vm.LastTaskItem = null!;
                vm.DetailsAreOpen = false;
                RunLayoutJobs();
                await Assert.That(splitView.IsPaneOpen).IsFalse();

                var createMenuButton = FindControlByAutomationId<DropDownButton>(view, "GlobalTaskCreateMenuButton");
                await Assert.That(IsVisibleAndArranged(createMenuButton)).IsTrue();

                var handled = vm.TryHandleTaskCardBackGesture();
                RunLayoutJobs();

                await Assert.That(handled).IsTrue();
                await Assert.That(splitView.IsPaneOpen).IsTrue();
                await Assert.That(vm.CurrentTaskItem).IsEqualTo(task);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(360)]
    [Arguments(390)]
    public async Task CurrentTaskCard_PhoneWeeklyRepeaterLayout_FillsWeekdayRow(double width)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    width,
                    844,
                    MainWindowViewModelFixture.RepeateTask9Id,
                    task =>
                    {
                        task.Repeater!.Type = RepeaterType.Weekly;
                        task.Repeater.WorkDays = true;
                    });
                window = createdWindow;

                var scrollViewer = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
                var card = FindControlByAutomationId<Control>(view, "CurrentTaskCard");
                var repeaterSection = FindControlByAutomationId<Control>(view, "CurrentTaskRepeaterSection");
                var repeaterSelector = FindControlByAutomationId<ComboBox>(view, "CurrentTaskRepeaterSelector");
                var patternTypeSelector = FindControlByAutomationId<ComboBox>(view, "CurrentTaskRepeaterPatternTypeSelector");
                var periodInput = FindControlByAutomationId<NumericUpDown>(view, "CurrentTaskRepeaterPeriodInput");
                var afterCompleteCheckBox = FindControlByAutomationId<CheckBox>(view, "CurrentTaskRepeaterAfterCompleteCheckBox");
                var weekdayPanel = view.GetVisualDescendants()
                    .OfType<WrapPanel>()
                    .FirstOrDefault(panel =>
                        panel.Classes.Contains("WeekdayToggles") &&
                        IsVisibleAndArranged(panel))
                    ?? throw new InvalidOperationException("Visible phone weekday toggle panel was not found.");
                var weekdayToggles = weekdayPanel.GetVisualDescendants()
                    .OfType<ToggleButton>()
                    .Where(static toggle => toggle.Classes.Contains("WeekdayToggle"))
                    .Where(IsVisibleAndArranged)
                    .ToArray();

                if (weekdayToggles.Length != 7)
                {
                    throw new InvalidOperationException(
                        $"Expected seven visible phone weekday toggles, found {weekdayToggles.Length}.");
                }

                var firstTop = GetTopEdge(view, weekdayToggles[0]);
                var wrappedToggle = weekdayToggles
                    .Select(toggle => new { Toggle = toggle, Top = GetTopEdge(view, toggle) })
                    .FirstOrDefault(item => Math.Abs(item.Top - firstTop) > 2);
                if (wrappedToggle is not null)
                {
                    throw new InvalidOperationException(
                        $"Phone weekday toggles should stay in one filled row: " +
                        $"content={wrappedToggle.Toggle.Content}; firstTop={firstTop:F1}; top={wrappedToggle.Top:F1}; " +
                        $"viewport={scrollViewer.Viewport}; panel={weekdayPanel.Bounds}; toggleWidth={weekdayToggles[0].Width:F1}.");
                }

                var repeaterSelectorTop = GetTopEdge(view, repeaterSelector);
                var patternTypeTop = GetTopEdge(view, patternTypeSelector);
                if (Math.Abs(patternTypeTop - repeaterSelectorTop) > 2)
                {
                    throw new InvalidOperationException(
                        "Phone repeater template and type selectors should share the first compact row: " +
                        $"templateTop={repeaterSelectorTop:F1}; typeTop={patternTypeTop:F1}.");
                }

                AssertRowUsesRightEdge(
                    repeaterSection,
                    [repeaterSelector, patternTypeSelector],
                    8d,
                    "Phone repeater selector row");
                AssertRowUsesRightEdge(weekdayPanel, weekdayToggles, 8d, "Phone weekday toggle row");
                AssertRowUsesRightEdge(
                    repeaterSection,
                    [periodInput, afterCompleteCheckBox],
                    8d,
                    "Phone repeater period row");
                AssertNoHorizontalOverflow(scrollViewer, card);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(360)]
    [Arguments(390)]
    [Arguments(430)]
    public async Task CurrentTaskCreateMenu_PhoneWidth_UsesTouchFriendlyMenuItems(double width)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, width, 844);
                window = createdWindow;

                var createMenuButton = FindControlByAutomationId<DropDownButton>(view, "GlobalTaskCreateMenuButton");
                AssertCreateMenuContainsTaskCommands(createMenuButton);
                AssertCreateMenuUsesTouchFriendlyItems(createMenuButton);
                AssertHorizontallyContained(view, createMenuButton);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(360)]
    [Arguments(390)]
    [Arguments(430)]
    public async Task CurrentTaskCard_PhoneWidthLayout_DoesNotOverflowAndKeepsRelationEditorUsable(double width)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                ConfigureLongEmojiAncestorChain(fixture);
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    width,
                    844,
                    MainWindowViewModelFixture.SubTask22Id);
                window = createdWindow;

                var scrollViewer = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
                var card = FindControlByAutomationId<Control>(view, "CurrentTaskCard");
                var parentEmojiTrail = FindControlByAutomationId<EmojiTextBlock>(card, "CurrentTaskParentEmojiTrail");
                // Exercise the storage echo that can arrive from the fixture's autosave.
                // The ancestor chain must survive it before we check the layout.
                await fixture.MainWindowViewModelTest.taskRepository!.Update(
                    (TaskItemViewModel)parentEmojiTrail.DataContext!);
                ArrangeMainControlForTest(window, view, width, 844);
                EnsureDetailsPaneArranged(window, view, width, 844);

                var commandBar = FindControlByAutomationId<Control>(view, "CurrentTaskCommandBar");
                var header = FindControlByAutomationId<Control>(card, "CurrentTaskHeader");
                var title = FindControlByAutomationId<TextBox>(card, "CurrentTaskTitleTextBox");
                var createMenuButton = FindControlByAutomationId<DropDownButton>(view, "GlobalTaskCreateMenuButton");
                var actionsMenuButton = FindControlByAutomationId<DropDownButton>(view, "CurrentTaskActionsMenuButton");

                AssertNoHorizontalOverflow(scrollViewer, card);
                AssertFirstPhoneViewportShowsHeader(scrollViewer, commandBar, header, title);
                AssertTaskHeaderAlignment(header);
                await Assert.That(IsVisibleAndArranged(parentEmojiTrail)).IsTrue();
                await Assert.That(parentEmojiTrail.EmojiText).IsEqualTo(LongEmojiAncestorTrail);
                AssertHorizontallyContained(scrollViewer, parentEmojiTrail);
                AssertHasClass(createMenuButton, "TaskCreateMenuButton");
                AssertCreateMenuContainsTaskCommands(createMenuButton);
                AssertHorizontallyContained(view, createMenuButton);
                AssertHasClass(actionsMenuButton, "TaskActionsMenuButton");
                AssertActionsMenuContainsTaskCommands(actionsMenuButton);
                await Assert.That(IsVisibleAndArranged(actionsMenuButton)).IsTrue();

                foreach (var automationId in KeyControlAutomationIds)
                {
                    var control = FindControlByAutomationId<Control>(card, automationId);
                    await Assert.That(control.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(control.Bounds.Height).IsGreaterThan(0);
                    AssertHorizontallyContained(scrollViewer, control);
                }

                var parentsAddButton = FindControlByAutomationId<Button>(card, "CurrentTaskParentsRelationAddButton");
                AssertHasClass(parentsAddButton, "RelationAddButton");
                await Assert.That(parentsAddButton.Content?.ToString()).IsEqualTo("＋");
                await Assert.That(parentsAddButton.Bounds.Width).IsLessThanOrEqualTo(40);
                parentsAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunLayoutJobs();

                var relationInput = FindControlByAutomationId<TextBox>(card, "CurrentTaskParentsRelationAddInput");
                var relationSuggestions = FindControlByAutomationId<ListBox>(card, "CurrentTaskParentsRelationSuggestions");
                var relationCancel = FindControlByAutomationId<Button>(card, "CurrentTaskParentsRelationAddCancelButton");
                var relationConfirm = FindControlByAutomationId<Button>(card, "CurrentTaskParentsRelationAddConfirmButton");

                await Assert.That(IsVisibleAndArranged(relationInput)).IsTrue();
                await Assert.That(IsVisibleAndArranged(relationSuggestions)).IsTrue();
                await Assert.That(IsVisibleAndArranged(relationCancel)).IsTrue();
                await Assert.That(IsVisibleAndArranged(relationConfirm)).IsTrue();
                AssertHorizontallyContained(scrollViewer, relationInput);
                AssertHorizontallyContained(scrollViewer, relationSuggestions);
                AssertHorizontallyContained(scrollViewer, relationCancel);
                AssertHorizontallyContained(scrollViewer, relationConfirm);

                AssertNoHorizontalOverflow(scrollViewer, card);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static void AssertFirstPhoneViewportShowsHeader(
        ScrollViewer scrollViewer,
        Control commandBar,
        Control header,
        Control title)
    {
        var commandBarBottom = GetBottomEdge(scrollViewer, commandBar);
        var commandBarTop = GetTopEdge(scrollViewer, commandBar);
        var headerTop = GetTopEdge(scrollViewer, header);
        var headerBottom = GetBottomEdge(scrollViewer, header);
        var titleTop = GetTopEdge(scrollViewer, title);

        if (commandBarTop < headerTop - 1 || commandBarBottom > headerBottom + 1)
        {
            throw new InvalidOperationException(
                $"Phone command bar should live inside the task header: " +
                $"commandTop={commandBarTop:F1}; commandBottom={commandBarBottom:F1}; " +
                $"headerTop={headerTop:F1}; headerBottom={headerBottom:F1}.");
        }

        if (commandBarBottom > 160)
        {
            throw new InvalidOperationException(
                $"Phone command bar consumes too much first viewport height: bottom={commandBarBottom:F1}.");
        }

        if (headerTop >= scrollViewer.Bounds.Height || titleTop >= scrollViewer.Bounds.Height)
        {
            throw new InvalidOperationException(
                $"Task card header is not visible in the first phone viewport: " +
                $"headerTop={headerTop:F1}; titleTop={titleTop:F1}; viewport={scrollViewer.Bounds.Height:F1}.");
        }
    }

    [Test]
    public async Task CurrentTaskCard_TaskHistory_ExposesGitChangesAndStatusModes()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 1400, 900);
                window = createdWindow;
                var expander = FindControlByAutomationId<Expander>(view, "StatusHistoryExpander");
                expander.IsExpanded = true;
                RunLayoutJobs();

                var gitMode = FindControlByAutomationId<ToggleButton>(view, "TaskHistoryGitModeButton");
                var statusMode = FindControlByAutomationId<ToggleButton>(view, "TaskHistoryStatusModeButton");
                var options = FindControlByAutomationId<Button>(view, "TaskHistoryOptionsButton");
                var metadata = (CheckBox)((Flyout)options.Flyout!).Content!;
                var refresh = FindControlByAutomationId<Button>(view, "TaskHistoryRefreshButton");
                var gitPanel = FindControlByAutomationId<Control>(view, "TaskGitHistoryPanel");
                var gitItems = FindControlByAutomationId<ItemsControl>(view, "TaskHistoryItems");

                await Assert.That(gitMode.IsChecked).IsTrue();
                await Assert.That(statusMode.IsChecked).IsFalse();
                await Assert.That(IsVisibleAndArranged(options)).IsTrue();
                await Assert.That(TopLevel.GetTopLevel(metadata)).IsNull();
                options.Flyout!.ShowAt(options);
                RunLayoutJobs();
                await Assert.That(IsVisibleAndArranged(metadata)).IsTrue();
                metadata.IsChecked = true;
                await Assert.That(view.TaskHistory.ShowMetadata).IsTrue();
                metadata.IsChecked = false;
                options.Flyout.Hide();
                await Assert.That(IsVisibleAndArranged(refresh)).IsTrue();
                await Assert.That(IsVisibleAndArranged(gitPanel)).IsTrue();
                await Assert.That(gitItems).IsNotNull();

                view.TaskHistory.IsGitMode = false;
                RunLayoutJobs();

                var statusItems = FindControlByAutomationId<ItemsControl>(view, "StatusHistoryItems");
                await Assert.That(IsVisibleAndArranged(statusItems)).IsTrue();
                await Assert.That(gitPanel.IsVisible).IsFalse();

                StopBackgroundTaskHistoryRefresh(view);
                view.TaskHistory.IsGitMode = true;
                view.TaskHistory.Entries.Add(new TaskHistoryEntry(
                    "1234567890",
                    "Test Author",
                    DateTimeOffset.UtcNow,
                    "git 1234567",
                    "change title",
                    [new TaskHistoryFieldChange(
                        "Title",
                        "Title",
                        "Before",
                        "After",
                        TaskHistoryChangeType.Modified,
                        IsMetadata: false)]));
                RunLayoutJobs();

                await Assert.That(IsVisibleAndArranged(gitPanel)).IsTrue();
                await Assert.That(view.TaskHistory.Entries.Count).IsEqualTo(1);
                await Assert.That(gitItems.GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Any(value => IsVisibleAndArranged(value) && value.Text == "After")).IsTrue();
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_TaskHistory_AdaptsAtThreeWidthsInBothThemes()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RenderedTaskHistoryAppBuilder));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var app = Application.Current!;
            var previousTheme = app.RequestedThemeVariant;
            try
            {
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    app.RequestedThemeVariant = theme;
                    foreach (var width in new[] { 360d, 480d, 900d })
                    {
                        var fixture = new MainWindowViewModelFixture();
                        Window? window = null;
                        try
                        {
                            var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, width, 900);
                            window = createdWindow;
                            var expander = FindControlByAutomationId<Expander>(view, "StatusHistoryExpander");
                            view.TaskHistory.IsGitMode = false;
                            expander.IsExpanded = true;
                            RunLayoutJobs();
                            view.TaskHistory.IsGitMode = true;
                            StopBackgroundTaskHistoryRefresh(view);
                            view.TaskHistory.Entries.Add(new TaskHistoryEntry(
                                "1234567890", "Test Author", DateTimeOffset.UtcNow, "git 1234567", "change fields",
                                [new TaskHistoryFieldChange("Title", "Title", "Before", "After", TaskHistoryChangeType.Modified, false),
                                 new TaskHistoryFieldChange("Description", "Description", "Old description", "New description", TaskHistoryChangeType.Modified, false),
                                 new TaskHistoryFieldChange("LongDescription", "Long description", new string('c', 60) + "…", new string('d', 60) + "…", TaskHistoryChangeType.Modified, false)]));
                            RunLayoutJobs();

                            await Assert.That(IsVisibleAndArranged(expander)).IsTrue();
                            var values = FindControlByAutomationId<ItemsControl>(view, "TaskHistoryItems")
                                .GetVisualDescendants().OfType<SelectableTextBlock>().Where(IsVisibleAndArranged).ToArray();
                            var before = values.Single(value => value.Text == "Before");
                            var after = values.Single(value => value.Text == "After");
                            var beforeY = before.TranslatePoint(default, expander)?.Y ?? double.NaN;
                            var afterY = after.TranslatePoint(default, expander)?.Y ?? double.NaN;
                            await Assert.That(Math.Abs(afterY - beforeY)).IsLessThan(1);
                            await Assert.That(values.All(value => value.Bounds.Width <= expander.Bounds.Width)).IsTrue();
                            var longPreview = values.Single(value => value.Text == new string('d', 60) + "…");
                            await Assert.That(longPreview.MaxLines).IsEqualTo(2);
                            await Assert.That(longPreview.TextLayout.TextLines.Count).IsLessThanOrEqualTo(2);
                            var oldLongPreview = values.Single(value => value.Text == new string('c', 60) + "…");
                            if (longPreview.FindAncestorOfType<TaskHistoryFieldChangeView>()!.Bounds.Width >= 440)
                                await Assert.That(Math.Abs(GetTop(longPreview, expander) - GetTop(oldLongPreview, expander))).IsLessThan(1);
                            else
                                await Assert.That(Math.Abs(GetLeftEdge(expander, longPreview) - GetLeftEdge(expander, oldLongPreview))).IsLessThan(1);
                            foreach (var value in values)
                                await Assert.That(GetRightEdge(expander, value)).IsLessThanOrEqualTo(expander.Bounds.Width + 1);
                            await Assert.That(ResolveResourceColor(view, "TaskHistoryOldValueBrush", "old value"))
                                .IsNotEqualTo(ResolveResourceColor(view, "TaskHistoryNewValueBrush", "new value"));
                            if (theme == ThemeVariant.Light && (width == 480 || width == 900) &&
                                Environment.GetEnvironmentVariable("UNLIMOTION_TEST_CAPTURE_FRAMES") == "1")
                            {
                                var renderedEntries = view.TaskHistory.Entries.ToArray();
                                view.TaskHistory.Dispose();
                                foreach (var entry in renderedEntries)
                                    view.TaskHistory.Entries.Add(entry);
                                expander.BringIntoView();
                                RunLayoutJobs();
                                var directory = Environment.GetEnvironmentVariable("UNLIMOTION_TEST_TRACE_DIRECTORY")
                                    ?? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-evidence", "task-history");
                                Directory.CreateDirectory(directory);
                                using var frame = window.CaptureRenderedFrame();
                                if (frame is not null)
                                {
                                    frame.Save(Path.Combine(directory, $"after-headless-history-{width:0}.png"));
                                }
                                if (width == 900)
                                {
                                    var showDetails = view.GetVisualDescendants().OfType<Button>()
                                        .Single(button => AutomationProperties.GetAutomationId(button) == "TaskHistoryShowDetailsButton"
                                            && IsVisibleAndArranged(button));
                                    await Assert.That((showDetails.DataContext as TaskHistoryFieldChange)?.FieldPath)
                                        .IsEqualTo("LongDescription");
                                    showDetails.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                    RunLayoutJobs();
                                    using var detailFrame = window.CaptureRenderedFrame();
                                    detailFrame?.Save(Path.Combine(directory, "after-headless-full-value.png"));
                                }
                            }
                        }
                        finally
                        {
                            CloseWindow(window);
                            await fixture.CleanTasksAsync();
                        }
                    }
                }
            }
            finally
            {
                app.RequestedThemeVariant = previousTheme;
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_TaskHistory_InlineDetailsPreserveContextAndClose()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RenderedTaskHistoryAppBuilder));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            var valuePath = Path.GetTempFileName();
            try
            {
                var oldText = string.Join(" ", Enumerable.Repeat("Old full value", 25));
                var newText = string.Join(" ", Enumerable.Repeat("New full value", 30));
                var bytes = Encoding.UTF8.GetBytes(new JObject { ["Old"] = oldText, ["New"] = newText }.ToString());
                File.WriteAllBytes(valuePath, bytes);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                // Prepare the start date in storage so showing the card does not autosave it
                // and refresh away the deterministic history rows during the interaction.
                var taskPath = Path.Combine(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask2Id);
                var taskJson = JObject.Parse(File.ReadAllText(taskPath));
                taskJson[nameof(TaskItem.PlannedBeginDateTime)] = DateTime.Today;
                File.WriteAllText(taskPath, taskJson.ToString());
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 900, 900);
                window = createdWindow;
                window.Activate();
                var expander = FindControlByAutomationId<Expander>(view, "StatusHistoryExpander");
                view.TaskHistory.IsGitMode = false;
                expander.IsExpanded = true;
                RunLayoutJobs();
                view.TaskHistory.IsGitMode = true;
                StopBackgroundTaskHistoryRefresh(view);
                var change = new TaskHistoryFieldChange("Description", "Description", oldText[..60] + "…", newText[..60] + "…",
                    TaskHistoryChangeType.Modified, false,
                    new TaskHistoryValueReference("", null, valuePath, "Old", true, hash),
                    new TaskHistoryValueReference("", null, valuePath, "New", true, hash));
                const string sha = "1234567890123456789012345678901234567890";
                view.TaskHistory.Entries.Add(new TaskHistoryEntry(sha, "Author", DateTimeOffset.UtcNow, "Git 1234567", "Edit content",
                    [change, change with { FieldPath = "Title", DisplayName = "Title" }]));
                RunLayoutJobs();
                var fields = view.GetVisualDescendants().OfType<TaskHistoryFieldChangeView>().Where(IsVisibleAndArranged).ToArray();
                await Assert.That(fields.Length).IsEqualTo(2);
                async Task Open(TaskHistoryFieldChangeView field)
                {
                    var show = FindControlByAutomationId<Button>(field, "TaskHistoryShowDetailsButton");
                    show.Focus();
                    await Assert.That(show.IsFocused).IsTrue();
                    show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (var attempt = 0; attempt < 200 && !field.IsDetailsExpanded; attempt++)
                        await Task.Delay(10);
                    RunLayoutJobs();
                    Console.WriteLine($"Inline details: field={((TaskHistoryFieldChange)field.DataContext!).FieldPath}; expanded={field.IsDetailsExpanded}; attached={TopLevel.GetTopLevel(field) is not null}; visible={field.IsVisible}; model details={view.TaskHistory.HasDetails}; rows={view.TaskHistory.Entries.Count}");
                    await Assert.That(field.IsDetailsExpanded).IsTrue();
                    var collapse = FindControlByAutomationId<Button>(field, "TaskHistoryCloseDetailsButton");
                    for (var attempt = 0; attempt < 200 && !collapse.IsFocused; attempt++)
                    {
                        RunLayoutJobs();
                        await Task.Delay(10);
                    }
                    await Assert.That(collapse.IsFocused).IsTrue();
                }
                await Open(fields[0]);
                await Assert.That(fields[0].GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Any(value => IsVisibleAndArranged(value) && value.Text == newText)).IsTrue();
                var panel = FindControlByAutomationId<Control>(fields[0], "TaskHistoryDetailsPanel");
                await Assert.That(panel.TranslatePoint(default, fields[0])!.Value.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(panel.Bounds.Width).IsLessThanOrEqualTo(fields[0].Bounds.Width);
                await Open(fields[1]);
                await Assert.That(fields[0].IsDetailsExpanded).IsFalse();
                await Assert.That(fields[0].GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Any(value => value.Text == newText)).IsFalse();
                FindControlByAutomationId<Button>(fields[1], "TaskHistoryCloseDetailsButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunLayoutJobs();
                await Assert.That(fields.All(field => !field.IsDetailsExpanded)).IsTrue();
                await Assert.That(view.TaskHistory.HasDetails).IsFalse();
                await Assert.That(view.TaskHistory.DetailNewValue).IsEmpty();
                await Open(fields[0]);
                FindControlByAutomationId<ToggleButton>(view, "TaskHistoryStatusModeButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunLayoutJobs();
                await Assert.That(fields[0].IsDetailsExpanded).IsFalse();
                await Assert.That(view.TaskHistory.HasDetails).IsFalse();
                view.TaskHistory.IsGitMode = true;
                RunLayoutJobs();
                var copy = FindControlByAutomationId<Button>(view, "TaskHistoryCopyCommitButton");
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(20);
                await Assert.That(await window.Clipboard!.TryGetTextAsync()).IsEqualTo(sha);
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
                File.Delete(valuePath);
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_TaskHistory_StaysDockedWhileTaskAndHistoryScroll()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RenderedTaskHistoryAppBuilder));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(360, 480), new Size(900, 900) })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    var fixture = new MainWindowViewModelFixture();
                    Window? window = null;
                    try
                    {
                        var taskPath = Path.Combine(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask2Id);
                        var taskJson = JObject.Parse(File.ReadAllText(taskPath));
                        taskJson[nameof(TaskItem.PlannedBeginDateTime)] = DateTime.Today;
                        taskJson[nameof(TaskItem.Description)] = string.Join("\n", Enumerable.Repeat("Long task content", 80));
                        File.WriteAllText(taskPath, taskJson.ToString());
                        var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, size.Width, size.Height);
                        window = createdWindow;
                        var frame = FindControlByAutomationId<Border>(view, "CurrentTaskDetailsPanelFrame");
                        var body = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
                        var footer = FindControlByAutomationId<Border>(view, "CurrentTaskStatusHistorySection");
                        var expander = FindControlByAutomationId<Expander>(view, "StatusHistoryExpander");
                        view.TaskHistory.IsGitMode = false;
                        expander.IsExpanded = true;
                        RunLayoutJobs();
                        StopBackgroundTaskHistoryRefresh(view);
                        view.TaskHistory.IsGitMode = true;
                        FindControlByAutomationId<ItemsControl>(view, "StatusHistoryItems").ItemsSource = Enumerable.Range(0, 40)
                            .Select(index => new TaskStatusHistoryEntry { Author = "Author", ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-index) });
                        foreach (var expanded in new[] { false, true })
                        foreach (var gitMode in new[] { true, false })
                        {
                            view.TaskHistory.IsGitMode = false;
                            expander.IsExpanded = expanded;
                            RunLayoutJobs();
                            // Closing the real panel disposes its rows; reseed each deterministic layout state.
                            view.TaskHistory.Dispose();
                            view.TaskHistory.IsGitMode = gitMode;
                            for (var index = 0; index < 40; index++)
                                view.TaskHistory.Entries.Add(new TaskHistoryEntry($"commit-{index}", "Author", DateTimeOffset.UtcNow,
                                    "Git", $"Change {index}", [new TaskHistoryFieldChange("Title", "Title", "Before", "After", TaskHistoryChangeType.Modified, false)]));
                            RunLayoutJobs();
                            var footerTop = GetTopEdge(frame, footer);
                            var footerBottom = GetBottomEdge(frame, footer);
                            await Assert.That(footerTop).IsGreaterThanOrEqualTo(0);
                            await Assert.That(footerBottom).IsLessThanOrEqualTo(frame.Bounds.Height);
                            await Assert.That(frame.Bounds.Height - footerBottom).IsLessThanOrEqualTo(24);
                            await Assert.That(body.Extent.Height).IsGreaterThan(body.Viewport.Height);
                            foreach (var fraction in new[] { 0d, 0.5, 1d })
                            {
                                body.Offset = new Vector(0, (body.Extent.Height - body.Viewport.Height) * fraction);
                                RunLayoutJobs();
                                await Assert.That(Math.Abs(GetTopEdge(frame, footer) - footerTop)).IsLessThan(1);
                                await Assert.That(GetBottomEdge(frame, body)).IsLessThanOrEqualTo(footerTop + 1);
                                if (expanded)
                                {
                                    var list = FindControlByAutomationId<ListBox>(view, "TaskHistoryItems");
                                    var historyScroll = gitMode
                                        ? list.GetVisualDescendants().OfType<ScrollViewer>().Single(control => ReferenceEquals(control.TemplatedParent, list))
                                        : FindControlByAutomationId<ScrollViewer>(view, "TaskHistoryStatusScrollViewer");
                                    Console.WriteLine($"Docked history: theme={theme}; size={size}; git={gitMode}; footer={footer.Bounds}; viewport={historyScroll.Viewport}; extent={historyScroll.Extent}");
                                    await Assert.That(historyScroll.Viewport.Height).IsGreaterThan(0);
                                    await Assert.That(GetBottomEdge(frame, historyScroll)).IsLessThanOrEqualTo(frame.Bounds.Height);
                                    var bodyOffset = body.Offset;
                                    await Assert.That(historyScroll.Extent.Height).IsGreaterThan(historyScroll.Viewport.Height);
                                    historyScroll.ScrollToEnd();
                                    RunLayoutJobs();
                                    await Assert.That(body.Offset).IsEqualTo(bodyOffset);
                                    await Assert.That(Math.Abs(GetTopEdge(frame, footer) - footerTop)).IsLessThan(1);
                                }
                            }
                        }
                        fixture.MainWindowViewModelTest.CurrentTaskItem = null;
                        RunLayoutJobs();
                        await Assert.That(footer.IsEffectivelyVisible).IsFalse();
                    }
                    finally
                    {
                        CloseWindow(window);
                        await fixture.CleanTasksAsync();
                    }
                }
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_TaskHistory_HeaderAndContentHaveCleanChrome()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RenderedTaskHistoryAppBuilder));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var width in new[] { 360d, 900d })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    var fixture = new MainWindowViewModelFixture();
                    Window? window = null;
                    try
                    {
                        var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, width, 900);
                        window = createdWindow;
                        var expander = FindControlByAutomationId<Expander>(view, "StatusHistoryExpander");
                        view.TaskHistory.IsGitMode = false;
                        expander.IsExpanded = true;
                        RunLayoutJobs();
                        var content = expander.GetVisualDescendants().OfType<Border>()
                            .Single(border => ReferenceEquals(border.TemplatedParent, expander));
                        Console.WriteLine($"History content border: {content.BorderThickness}; theme={theme}; width={width}");
                        await Assert.That(content.BorderThickness).IsEqualTo(new Thickness(0));
                        var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "PART_HeaderSite");
                        foreach (var expanded in new[] { true, false })
                        {
                            expander.IsExpanded = expanded;
                            RunLayoutJobs();
                            var icon = header.GetVisualDescendants().OfType<PathIcon>().Single(control => control.IsEffectivelyVisible);
                            var title = header.GetVisualDescendants().OfType<ContentPresenter>().Single(control => control.Name == "TaskHistoryHeading");
                            var iconCenter = icon.TranslatePoint(new Point(icon.Bounds.Width / 2, icon.Bounds.Height / 2), header)!.Value.Y;
                            var titleCenter = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), header)!.Value.Y;
                            var iconRight = Math.Max(icon.TranslatePoint(default, header)!.Value.X,
                                icon.TranslatePoint(new Point(icon.Bounds.Width, icon.Bounds.Height), header)!.Value.X);
                            await Assert.That(Math.Abs(iconCenter - titleCenter)).IsLessThanOrEqualTo(1);
                            await Assert.That(icon.Bounds.Width).IsEqualTo(14);
                            await Assert.That(icon.Bounds.Height).IsEqualTo(14);
                            await Assert.That(Math.Abs(GetLeftEdge(header, title) - iconRight - 8)).IsLessThan(0.01);
                        }
                    }
                    finally
                    {
                        CloseWindow(window);
                        await fixture.CleanTasksAsync();
                    }
                }
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_TaskHistory_HeaderAutomationNameFollowsEachExpanderHeader()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 900, 900);
                window = createdWindow;
                var history = FindControlByAutomationId<Expander>(view, "StatusHistoryExpander");
                var details = new Expander { Header = "Operation details" };
                details.Classes.Add("TaskStatusHistoryExpander");
                ((Grid)view.Content!).Children.Add(details);
                RunLayoutJobs();

                var historyHeader = history.GetVisualDescendants().OfType<ToggleButton>()
                    .Single(button => button.Name == "PART_HeaderSite");
                var detailsHeader = details.GetVisualDescendants().OfType<ToggleButton>()
                    .Single(button => button.Name == "PART_HeaderSite");
                await Assert.That(AutomationProperties.GetName(historyHeader))
                    .IsEqualTo(LocalizationService.Current.Get("TaskHistory"));
                await Assert.That(AutomationProperties.GetName(detailsHeader)).IsEqualTo("Operation details");
                details.Header = "Read failure details";
                RunLayoutJobs();
                await Assert.That(AutomationProperties.GetName(detailsHeader)).IsEqualTo("Read failure details");
                await Assert.That(AutomationProperties.GetName(historyHeader))
                    .IsEqualTo(LocalizationService.Current.Get("TaskHistory"));
            }
            finally
            {
                CloseWindow(window);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_TaskHistory_CentersActionsAndScrollsSmoothlyWithinUnifiedBackground()
    {
        // Match the suite's drawing backend: cached glyphs from other Headless tests
        // cannot be rendered by Skia. Pixel evidence runs separately in a fresh Skia process.
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetTaskCardLayoutSharedState();
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var previousLogSink = Avalonia.Logging.Logger.Sink;
            Avalonia.Logging.Logger.Sink = new HistoryRenderDiagnosticLogSink();
            try
            {
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var width in new[] { 360d, 900d })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    var fixture = new MainWindowViewModelFixture();
                    Window? window = null;
                    try
                    {
                        var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, width, 900);
                        window = createdWindow;
                        var expander = FindControlByAutomationId<Expander>(view, "StatusHistoryExpander");
                        view.TaskHistory.IsGitMode = false;
                        expander.IsExpanded = true;
                        RunLayoutJobs();
                        StopBackgroundTaskHistoryRefresh(view);
                        view.TaskHistory.IsGitMode = true;
                        for (var index = 0; index < 40; index++)
                            view.TaskHistory.Entries.Add(new TaskHistoryEntry($"commit-{index}", "Author", DateTimeOffset.UtcNow,
                                "Git", $"Change {index}", [new TaskHistoryFieldChange("Title", "Title", "Before", "After", TaskHistoryChangeType.Modified, false)]));
                        RunLayoutJobs();
                        var footer = FindControlByAutomationId<Border>(view, "CurrentTaskStatusHistorySection");
                        var content = expander.GetVisualDescendants().OfType<Border>().Single(border => ReferenceEquals(border.TemplatedParent, expander));
                        await Assert.That(footer.Background).IsNotNull();
                        await Assert.That(content.Background is null || content.Background is ISolidColorBrush { Color.A: 0 }).IsTrue();
                        foreach (var id in new[] { "TaskHistoryGitModeButton", "TaskHistoryStatusModeButton", "TaskHistoryRefreshButton", "TaskHistoryOptionsButton", "TaskHistoryCopyCommitButton" })
                        {
                            var button = FindControlByAutomationId<Button>(view, id);
                            var text = button.GetVisualDescendants().OfType<TextBlock>().First();
                            var center = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), button)!.Value;
                            await Assert.That(Math.Abs(center.X - button.Bounds.Width / 2)).IsLessThanOrEqualTo(1);
                            await Assert.That(Math.Abs(center.Y - button.Bounds.Height / 2)).IsLessThanOrEqualTo(1);
                        }
                        var body = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
                        var footerTop = GetTopEdge(view, footer);
                        var bodyOffset = body.Offset;
                        var list = FindControlByAutomationId<ListBox>(view, "TaskHistoryItems");
                        var firstItem = list.GetVisualDescendants().OfType<ListBoxItem>().First(IsVisibleAndArranged);
                        await Assert.That(AutomationProperties.GetName(firstItem)).IsEqualTo("Change 0");
                        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(control => ReferenceEquals(control.TemplatedParent, list));
                        var offsets = new List<double>();
                        scroll.PropertyChanged += (_, args) =>
                        {
                            if (args.Property == ScrollViewer.OffsetProperty)
                                offsets.Add(scroll.Offset.Y);
                        };
                        var wheelPoint = await WaitForHistoryWheelTargetAsync(window, scroll);
                        window.MouseMove(wheelPoint);
                        expander.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, (_, args) =>
                        {
                            var candidates = (args.Source as Visual)?.GetVisualAncestors().OfType<ScrollViewer>()
                                .Select(candidate => $"{candidate.Name}: extent={candidate.Extent.Height}; viewport={candidate.Viewport.Height}; offset={candidate.Offset.Y}; bar={candidate.VerticalScrollBarVisibility}");
                            Console.WriteLine($"History wheel: source={args.Source}; handled={args.Handled}; {string.Join(" | ", candidates ?? [])}");
                        }, RoutingStrategies.Tunnel, handledEventsToo: true);
                        window.MouseWheel(wheelPoint, new Vector(0, -1));
                        await Assert.That(scroll.Offset.Y).IsLessThan(50);
                        for (var attempt = 0; attempt < 40 && Math.Abs(scroll.Offset.Y - 50) > 0.1; attempt++)
                        {
                            await Task.Delay(25);
                            RunLayoutJobs();
                        }
                        Console.WriteLine($"History wheel result: offset={scroll.Offset.Y}; rows={view.TaskHistory.Entries.Count}; samples={string.Join(",", offsets)}");
                        await Assert.That(Math.Abs(scroll.Offset.Y - 50)).IsLessThan(1);
                        await Assert.That(offsets.Any(offset => offset > 0 && offset < 49)).IsTrue();
                        for (var index = 0; index < 3; index++)
                            window.MouseWheel(wheelPoint, new Vector(0, -1));
                        for (var attempt = 0; attempt < 40 && Math.Abs(scroll.Offset.Y - 200) > 0.1; attempt++)
                        {
                            await Task.Delay(25);
                            RunLayoutJobs();
                        }
                        await Assert.That(Math.Abs(scroll.Offset.Y - 200)).IsLessThan(1);
                        window.MouseWheel(wheelPoint, new Vector(0, -1));
                        scroll.ScrollToHome();
                        RunLayoutJobs();
                        await Task.Delay(220);
                        RunLayoutJobs();
                        await Assert.That(scroll.Offset.Y).IsEqualTo(0);
                        await Assert.That(body.Offset).IsEqualTo(bodyOffset);
                        await Assert.That(Math.Abs(GetTopEdge(view, footer) - footerTop)).IsLessThan(1);

                        // Wheel input over full text belongs to that text area, not the commit list.
                        var longText = string.Join("\n", Enumerable.Repeat("Long history value", 60));
                        var longChange = new TaskHistoryFieldChange("Description", "Description", longText, longText,
                            TaskHistoryChangeType.Modified, false);
                        view.TaskHistory.Entries[0] = new TaskHistoryEntry("full-text", "Author", DateTimeOffset.UtcNow,
                            "Git", "Long description", [longChange]);
                        RunLayoutJobs();
                        scroll.ScrollToHome();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        RunLayoutJobs();
                        var field = list.GetVisualDescendants().OfType<TaskHistoryFieldChangeView>()
                            .Single(control => ReferenceEquals(control.DataContext, longChange));
                        FindControlByAutomationId<Button>(field, "TaskHistoryShowDetailsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        for (var attempt = 0; attempt < 100 && !field.IsDetailsExpanded; attempt++)
                            await Task.Delay(10);
                        RunLayoutJobs();
                        await Assert.That(field.IsDetailsExpanded).IsTrue();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        RunLayoutJobs();
                        var fullText = field.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "FullOldViewer");
                        await Assert.That(fullText.Extent.Height).IsGreaterThan(fullText.Viewport.Height);
                        var commitOffset = scroll.Offset;
                        var fullTextWheelPoint = await WaitForHistoryWheelTargetAsync(window, fullText);
                        window.MouseWheel(fullTextWheelPoint, new Vector(0, -1));
                        for (var attempt = 0; attempt < 40 && Math.Abs(fullText.Offset.Y - 50) > 0.1; attempt++)
                        {
                            await Task.Delay(25);
                            RunLayoutJobs();
                        }
                        await Assert.That(Math.Abs(fullText.Offset.Y - 50)).IsLessThan(1);
                        await Assert.That(scroll.Offset).IsEqualTo(commitOffset);

                        view.TaskHistory.IsGitMode = false;
                        FindControlByAutomationId<ItemsControl>(view, "StatusHistoryItems").ItemsSource = Enumerable.Range(0, 40)
                            .Select(index => new TaskStatusHistoryEntry { Author = "Author", ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-index) });
                        RunLayoutJobs();
                        var statuses = FindControlByAutomationId<ScrollViewer>(view, "TaskHistoryStatusScrollViewer");
                        var statusOffsets = new List<double>();
                        statuses.PropertyChanged += (_, args) =>
                        {
                            if (args.Property == ScrollViewer.OffsetProperty)
                                statusOffsets.Add(statuses.Offset.Y);
                        };
                        var statusWheelPoint = await WaitForHistoryWheelTargetAsync(window, statuses);
                        window.MouseWheel(statusWheelPoint, new Vector(0, -1));
                        for (var attempt = 0; attempt < 40 && Math.Abs(statuses.Offset.Y - 50) > 0.1; attempt++)
                        {
                            await Task.Delay(25);
                            RunLayoutJobs();
                        }
                        await Assert.That(Math.Abs(statuses.Offset.Y - 50)).IsLessThan(1);
                        await Assert.That(statusOffsets.Any(offset => offset > 0 && offset < 49)).IsTrue();
                        await Assert.That(body.Offset).IsEqualTo(bodyOffset);
                        Console.WriteLine($"Smooth history wheel: theme={theme}; width={width}; Git samples={offsets.Count}; status samples={statusOffsets.Count}; nested offset={fullText.Offset.Y}");
                    }
                    finally
                    {
                        CloseWindow(window);
                        await fixture.CleanTasksAsync();
                    }
                }
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                Avalonia.Logging.Logger.Sink = previousLogSink;
            }
        }, CancellationToken.None);
    }

    private static async Task<Point> WaitForHistoryWheelTargetAsync(Window window, ScrollViewer scroll)
    {
        Point point = default;
        Point? previousPoint = null;
        Visual? hit = null;
        // Bounds can precede the compositor's input scene. Require two stable composition
        // positions and a hit inside this viewport before sending physical wheel input.
        var ready = await TestHelpers.WaitUntilAsync(() =>
        {
            RunLayoutJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            RunLayoutJobs();
            var translated = scroll.TranslatePoint(new Point(24, 24), window);
            if (!translated.HasValue)
                return false;
            point = translated.Value;
            hit = window.InputHitTest(point) as Visual;
            var inViewport = ReferenceEquals(hit, scroll) ||
                hit?.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, scroll)) == true;
            var stable = previousPoint == point;
            previousPoint = point;
            return stable && inViewport && scroll.IsEffectivelyVisible;
        }, TimeSpan.FromSeconds(3));
        if (!ready)
        {
            Console.WriteLine($"History wheel target not ready: window={window.Bounds}; scroll={scroll.Bounds}; point={point}; hit={hit}; visible={scroll.IsEffectivelyVisible}; disabledHit={window.InputHitTest(point, enabledElementsOnly: false)}");
            Console.WriteLine("History wheel ancestors: " + string.Join(" | ", scroll.GetVisualAncestors().OfType<Control>()
                .Select(control => $"{control.GetType().Name}/{control.Name}: visible={control.IsVisible}; enabled={control.IsEffectivelyEnabled}; hit={control.IsHitTestVisible}; opacity={control.Opacity}; bounds={control.Bounds}")));
        }
        await Assert.That(ready).IsTrue();
        return point;
    }

    private sealed class HistoryRenderDiagnosticLogSink : Avalonia.Logging.ILogSink
    {
        public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area) =>
            level >= Avalonia.Logging.LogEventLevel.Warning && area != "Binding";

        public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate) =>
            Console.WriteLine($"Avalonia {level}/{area}: {messageTemplate}");

        public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate,
            params object?[] propertyValues) =>
            Console.WriteLine($"Avalonia {level}/{area}: {messageTemplate}; {string.Join(" | ", propertyValues)}");
    }

    private static void StopBackgroundTaskHistoryRefresh(MainControl view)
    {
        // These layout/interaction cases supply deterministic rows. Real Git/watchers are covered
        // by provider and desktop tests; their delayed notifications must not replace these rows.
        foreach (var name in new[] { "_taskHistoryWatcherSubscription", "_taskHistoryItemSubscription" })
            (typeof(MainControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view) as IDisposable)?.Dispose();
        view.TaskHistory.Dispose();
        RunLayoutJobs();
    }

    private static void PrepareTaskHistoryUiFixture(MainWindowViewModelFixture fixture, string selectedTaskId)
    {
        // These flows test the current UI, so prepare current-schema data before watchers start migrating it.
        var migrate = typeof(UnifiedTaskStorage).GetMethod("TryMigrateTaskStatusJson", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var file in Directory.EnumerateFiles(fixture.DefaultTasksFolderPath))
        {
            if (Path.GetFileName(file).StartsWith('.'))
                continue;
            var json = JObject.Parse(File.ReadAllText(file));
            if (json[nameof(TaskItem.Id)] is null)
                continue;
            var changed = (bool)migrate.Invoke(null, [json, DateTimeOffset.UtcNow])!;
            if (json[nameof(TaskItem.Id)]!.Value<string>() == selectedTaskId &&
                json[nameof(TaskItem.PlannedBeginDateTime)]?.Type is null or JTokenType.Null)
            {
                json[nameof(TaskItem.PlannedBeginDateTime)] = DateTime.Today;
                changed = true;
            }
            if (changed)
                File.WriteAllText(file, json.ToString());
        }
    }

    private static async Task<(MainControl View, Window Window)> CreateArrangedMainControlAsync(
        MainWindowViewModelFixture fixture,
        double width,
        double height,
        string selectedTaskId = MainWindowViewModelFixture.RootTask2Id,
        Action<TaskItemViewModel>? configureCurrentTask = null,
        double? fontSize = null)
    {
        PrepareTaskHistoryUiFixture(fixture, selectedTaskId);
        var vm = fixture.MainWindowViewModelTest;
        vm.Settings.LanguageMode = LocalizationService.EnglishLanguage;
        vm.Settings.FontSize = AppearanceSettings.DefaultFontSize;
        ResetApplicationLocalizedResources();
        ResetApplicationFontResources();
        await vm.Connect();
        vm.AllTasksMode = true;
        vm.DetailsAreOpen = true;
        var currentTask = TestHelpers.SetCurrentTask(vm, selectedTaskId);
        // Full-card layout checks include the repeater section, which requires a start date.
        currentTask.PlannedBeginDateTime ??= DateTime.Today;
        configureCurrentTask?.Invoke(currentTask);

        var view = new MainControl
        {
            DataContext = vm,
            Width = width,
            Height = height
        };
        if (fontSize.HasValue)
        {
            view.FontSize = fontSize.Value;
        }
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view
        };

        window.Show();
        try
        {
            ArrangeMainControlForTest(window, view, width, height);
            EnsureDetailsPaneArranged(window, view, width, height);
        }
        catch
        {
            window.Close();
            throw;
        }

        return (view, window);
    }

    private const int LongEmojiAncestorCount = 8;
    private static readonly string LongEmojiAncestorTrail = string.Concat(Enumerable.Repeat("🧭", LongEmojiAncestorCount));

    private static void ConfigureLongEmojiAncestorChain(MainWindowViewModelFixture fixture)
    {
        // Set up the actual storage input before Connect. VM-only relations disappear
        // when an autosave reapplies the authoritative graph from the task files.
        var taskPath = Path.Combine(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask22Id);
        var task = JObject.Parse(File.ReadAllText(taskPath));
        foreach (var parentId in task[nameof(TaskItem.ParentTasks)]!.Values<string>())
        {
            var parentPath = Path.Combine(fixture.DefaultTasksFolderPath, parentId!);
            var parent = JObject.Parse(File.ReadAllText(parentPath));
            var children = (JArray)parent[nameof(TaskItem.ContainsTasks)]!;
            foreach (var child in children.Where(child => child.Value<string>() == MainWindowViewModelFixture.SubTask22Id).ToArray())
            {
                child.Remove();
            }
            File.WriteAllText(parentPath, parent.ToString());
        }

        var ancestors = Enumerable.Range(0, LongEmojiAncestorCount)
            .Select(index => new TaskItem
            {
                Id = $"long-ancestor-{index:D2}-{Guid.NewGuid():N}",
                Title = $"🧭 Ancestor {index:D2}"
            })
            .ToArray();

        for (var index = 0; index < ancestors.Length; index++)
        {
            var ancestor = ancestors[index];
            ancestor.ContainsTasks = [index == ancestors.Length - 1
                ? MainWindowViewModelFixture.SubTask22Id
                : ancestors[index + 1].Id];
            ancestor.ParentTasks = index == 0 ? [] : [ancestors[index - 1].Id];
            File.WriteAllText(
                Path.Combine(fixture.DefaultTasksFolderPath, ancestor.Id),
                JsonConvert.SerializeObject(ancestor));
        }

        task[nameof(TaskItem.ParentTasks)] = new JArray(ancestors[^1].Id);
        File.WriteAllText(taskPath, task.ToString());
    }

    private static void ResetTaskCardLayoutSharedState()
    {
        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        LocalizationService.Current = localization;
        localization.SetLanguage(LocalizationService.EnglishLanguage);
        ResetApplicationLocalizedResources();
        ResetApplicationFontResources();
    }

    private static void ResetApplicationLocalizedResources()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        foreach (var key in LocalizationService.Current.GetResourceKeys(CultureInfo.InvariantCulture))
        {
            application.Resources[key] = LocalizationService.Current.Get(key);
        }
    }

    private static void ResetApplicationFontResources()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var resources = application.Resources;
        resources["AppFontSize"] = AppearanceSettings.DefaultFontSize;
        resources["AppSmallFontSize"] = AppearanceSettings.DefaultSmallFontSize;
        resources["AppTabFontSize"] = AppearanceSettings.DefaultTabFontSize;
        resources["AppTabMinHeight"] = AppearanceSettings.DefaultTabMinHeight;
        resources["AppSearchControlHeight"] = AppearanceSettings.DefaultSearchControlHeight;
        resources["AppSearchClearButtonSize"] = AppearanceSettings.DefaultSearchClearButtonSize;
        resources["AppSearchClearIconFontSize"] = AppearanceSettings.DefaultSearchClearIconFontSize;
        resources["AppSearchBarMinWidth"] = AppearanceSettings.DefaultSearchBarMinWidth;
        resources["AppFloatingControlMinHeight"] = AppearanceSettings.DefaultFloatingControlMinHeight;
    }

    private static void ArrangeMainControlForTest(Window window, MainControl view, double width, double height)
    {
        window.MinWidth = width;
        window.MinHeight = height;
        window.MaxWidth = width;
        window.MaxHeight = height;
        window.Width = width;
        window.Height = height;
        view.Width = width;
        view.Height = height;

        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        RunLayoutJobs();
    }

    private static void EnsureDetailsPaneArranged(Window window, MainControl view, double width, double height)
    {
        var splitView = view.GetVisualDescendants()
            .OfType<SplitView>()
            .FirstOrDefault();
        var detailsPanelFrame = FindControlByAutomationId<Border>(view, "CurrentTaskDetailsPanelFrame");
        var scrollViewer = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
        ApplyDetailsPaneTestWidth(scrollViewer, width);
        UpdateTaskDetailsLayoutForTest(view);

        if (splitView is not null)
        {
            splitView.OpenPaneLength = Math.Min(width, 600d);
            splitView.IsPaneOpen = true;
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (IsDetailsPaneArranged(detailsPanelFrame, scrollViewer))
            {
                return;
            }

            view.Width = width;
            view.Height = height;
            if (view.DataContext is MainWindowViewModel viewModel)
            {
                viewModel.DetailsAreOpen = true;
            }
            ApplyDetailsPaneTestWidth(scrollViewer, width);

            if (splitView is not null)
            {
                splitView.OpenPaneLength = Math.Min(width, 600d);
                splitView.IsPaneOpen = true;
            }

            ArrangeMainControlForTest(window, view, width, height);
            UpdateTaskDetailsLayoutForTest(view);
        }

        if (TryArrangeDetailsPaneFallback(view, detailsPanelFrame, scrollViewer, width, height))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Task details pane did not arrange to an open width: " +
            $"frame={detailsPanelFrame.Bounds}; scrollViewer={scrollViewer.Bounds}.");
    }

    private static void UpdateTaskDetailsLayoutForTest(MainControl view)
    {
        typeof(MainControl)
            .GetMethod("UpdateTaskDetailsLayout", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(view, null);
        RunLayoutJobs();
    }

    private static double ApplyDetailsPaneTestWidth(ScrollViewer scrollViewer, double width)
    {
        var detailsWidth = Math.Max(0d, Math.Min(width, 600d) - 20d);
        if (detailsWidth <= 100)
        {
            return detailsWidth;
        }

        // The card grid supplies a finite width shared by the body and docked footer.
        var frame = scrollViewer.GetVisualAncestors().OfType<Border>().First(control =>
            AutomationProperties.GetAutomationId(control) == "CurrentTaskDetailsPanelFrame");
        frame.Width = detailsWidth;
        frame.MinWidth = detailsWidth;
        frame.MaxWidth = detailsWidth;
        scrollViewer.Width = double.NaN;
        scrollViewer.MinWidth = 0;
        scrollViewer.MaxWidth = double.PositiveInfinity;
        return detailsWidth;
    }

    private static bool TryArrangeDetailsPaneFallback(
        MainControl view,
        Border detailsPanelFrame,
        ScrollViewer scrollViewer,
        double width,
        double height)
    {
        if (detailsPanelFrame.GetVisualParent() is not Grid paneRoot)
        {
            return false;
        }

        var paneWidth = Math.Min(width, 600d);
        var detailsWidth = ApplyDetailsPaneTestWidth(scrollViewer, width);
        if (paneWidth <= 100 || detailsWidth <= 100)
        {
            return false;
        }

        UpdateTaskDetailsLayoutForTest(view);
        paneRoot.Measure(new Size(paneWidth, height));
        paneRoot.Arrange(new Rect(0, 0, paneWidth, height));
        RunLayoutJobs();

        RunLayoutJobs();
        return IsDetailsPaneArranged(detailsPanelFrame, scrollViewer);
    }

    private static bool IsDetailsPaneArranged(Border detailsPanelFrame, ScrollViewer scrollViewer) =>
        detailsPanelFrame.IsVisible &&
        detailsPanelFrame.Bounds.Width > 100 &&
        detailsPanelFrame.Bounds.Height > 0 &&
        scrollViewer.IsVisible &&
        scrollViewer.Bounds.Width > 100 &&
        scrollViewer.Bounds.Height > 0;

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    automationId,
                    StringComparison.Ordinal));

        return control ?? throw new InvalidOperationException($"Control with AutomationId '{automationId}' was not found.");
    }

    private static void AssertHasClass(Control control, string className)
    {
        if (!control.Classes.Contains(className))
        {
            throw new InvalidOperationException(
                $"{control.GetType().Name}:{AutomationProperties.GetAutomationId(control)} " +
                $"does not have expected class '{className}'.");
        }
    }

    private static void AssertTaskDetailsPanelFrameUsesVisibleBorder(Border detailsPanelFrame)
    {
        AssertVisibleAndArranged(detailsPanelFrame, "CurrentTaskDetailsPanelFrame");

        if (detailsPanelFrame.BorderThickness != new Thickness(1))
        {
            throw new InvalidOperationException(
                $"Current task details panel frame should use a 1px border, got {detailsPanelFrame.BorderThickness}.");
        }

        AssertUsesResourceColor(
            detailsPanelFrame,
            detailsPanelFrame.BorderBrush,
            "SystemBaseMediumColor",
            "Current task details panel frame border");
        AssertUsesResourceColor(
            detailsPanelFrame,
            detailsPanelFrame.Background,
            "SystemChromeLowColor",
            "Current task details panel frame background");

        var borderColor = GetSolidBrushColor(detailsPanelFrame.BorderBrush ?? Brushes.Transparent);
        var backgroundColor = GetSolidBrushColor(detailsPanelFrame.Background ?? Brushes.Transparent);
        if (borderColor == backgroundColor)
        {
            throw new InvalidOperationException(
                $"Current task details panel frame border should contrast with its background, both resolved to {borderColor}.");
        }
    }

    private static void AssertTaskCardIsContentContainer(Border card)
    {
        if (card.BorderThickness != default)
        {
            throw new InvalidOperationException(
                $"Current task card should not draw its own panel border, got {card.BorderThickness}.");
        }

        if (card.BorderBrush is not null)
        {
            throw new InvalidOperationException("Current task card should not draw a separate panel border brush.");
        }

        if (card.Background is not null)
        {
            throw new InvalidOperationException("Current task card should not draw a separate panel background.");
        }
    }

    private static void AssertUsesResourceColor(
        Control resourceHost,
        IBrush? actualBrush,
        string resourceKey,
        string source)
    {
        if (actualBrush is null)
        {
            throw new InvalidOperationException($"{source} should use {resourceKey}, got null.");
        }

        var expectedColor = ResolveResourceColor(resourceHost, resourceKey, source);
        var actualColor = GetSolidBrushColor(actualBrush);
        if (actualColor != expectedColor)
        {
            throw new InvalidOperationException(
                $"{source} should use {resourceKey}: expected {expectedColor}, got {actualColor}.");
        }
    }

    private static Color ResolveResourceColor(Control resourceHost, string resourceKey, string source)
    {
        if (!resourceHost.TryGetResource(resourceKey, resourceHost.ActualThemeVariant, out var resource) &&
            Application.Current?.TryGetResource(resourceKey, resourceHost.ActualThemeVariant, out resource) != true)
        {
            throw new InvalidOperationException($"{source} resource '{resourceKey}' was not found.");
        }

        return resource switch
        {
            Color color => color,
            ISolidColorBrush solidColorBrush => solidColorBrush.Color,
            IBrush brush => GetSolidBrushColor(brush),
            _ => throw new InvalidOperationException(
                $"{source} resource '{resourceKey}' should resolve to a color or brush, got {resource?.GetType().Name ?? "null"}.")
        };
    }

    private static void AssertIconOnlyDropDownButton(DropDownButton button, string expectedContent, double expectedSize)
    {
        AssertIconOnlyButton(button, expectedContent, expectedSize);
        AssertHasClass(button, "TaskIconOnlyDropDownButton");

        var unexpectedChrome = button.GetVisualDescendants()
            .OfType<Control>()
            .Where(IsVisibleAndArranged)
            .Where(control =>
                control.GetType().Name.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                control.GetType().Name.Contains("Chevron", StringComparison.OrdinalIgnoreCase) ||
                control.GetType().Name.Contains("DropDownGlyph", StringComparison.OrdinalIgnoreCase) ||
                control is TextBlock textBlock &&
                !string.IsNullOrWhiteSpace(textBlock.Text) &&
                !string.Equals(textBlock.Text, expectedContent, StringComparison.Ordinal))
            .Select(control =>
                $"{control.GetType().Name}:{(control as TextBlock)?.Text ?? control.Name ?? string.Empty}")
            .ToArray();

        if (unexpectedChrome.Length > 0)
        {
            throw new InvalidOperationException(
                $"{button.GetType().Name}:{AutomationProperties.GetAutomationId(button)} should not render arrow chrome: " +
                string.Join("; ", unexpectedChrome));
        }
    }

    private static void AssertIconOnlyButton(ContentControl button, string expectedContent, double expectedSize)
    {
        if (!string.Equals(button.Content?.ToString(), expectedContent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{button.GetType().Name}:{AutomationProperties.GetAutomationId(button)} should render '{expectedContent}', " +
                $"got '{button.Content}'.");
        }

        if (Math.Abs(button.Bounds.Width - button.Bounds.Height) > 1 ||
            Math.Abs(button.Bounds.Width - expectedSize) > 1)
        {
            throw new InvalidOperationException(
                $"{button.GetType().Name}:{AutomationProperties.GetAutomationId(button)} should be a {expectedSize:F0}px square, " +
                $"bounds={button.Bounds}.");
        }

        var tooltip = ToolTip.GetTip(button)?.ToString();
        if (string.IsNullOrWhiteSpace(tooltip))
        {
            throw new InvalidOperationException(
                $"{button.GetType().Name}:{AutomationProperties.GetAutomationId(button)} should have a descriptive tooltip.");
        }

        var automationName = AutomationProperties.GetName(button);
        if (string.IsNullOrWhiteSpace(automationName))
        {
            throw new InvalidOperationException(
                $"{button.GetType().Name}:{AutomationProperties.GetAutomationId(button)} should have an automation name.");
        }
    }

    private static void AssertVectorRemoveButton(ContentControl button, double expectedSize)
    {
        AssertVisibleAndArranged((Control)button, AutomationProperties.GetAutomationId((Control)button) ?? string.Empty);

        if (button.Content is string content)
        {
            throw new InvalidOperationException(
                $"{button.GetType().Name}:{AutomationProperties.GetAutomationId((Control)button)} " +
                $"should use a vector remove icon, got text content '{content}'.");
        }

        if (Math.Abs(button.Bounds.Width - button.Bounds.Height) > 1 ||
            Math.Abs(button.Bounds.Width - expectedSize) > 1)
        {
            throw new InvalidOperationException(
                $"{button.GetType().Name}:{AutomationProperties.GetAutomationId((Control)button)} " +
                $"should be a {expectedSize:F0}px square, bounds={button.Bounds}.");
        }

        if (button is Button avaloniaButton)
        {
            if (avaloniaButton.BorderThickness != new Thickness(0))
            {
                throw new InvalidOperationException(
                    $"{button.GetType().Name}:{AutomationProperties.GetAutomationId((Control)button)} " +
                    $"should not render default button border, border={avaloniaButton.BorderThickness}.");
            }

            if (avaloniaButton.Background is ISolidColorBrush background &&
                background.Color != Colors.Transparent)
            {
                throw new InvalidOperationException(
                    $"{button.GetType().Name}:{AutomationProperties.GetAutomationId((Control)button)} " +
                    $"should use a transparent background, background={background.Color}.");
            }
        }

        var icons = ((Control)button).GetVisualDescendants()
            .OfType<PathIcon>()
            .Where(IsVisibleAndArranged)
            .ToArray();
        if (icons.Length != 1)
        {
            throw new InvalidOperationException(
                $"{button.GetType().Name}:{AutomationProperties.GetAutomationId((Control)button)} " +
                $"should render exactly one visible vector icon, found {icons.Length}.");
        }

        var icon = icons[0];
        var buttonCenterX = GetLeftEdge((Control)button, (Control)button) + button.Bounds.Width / 2;
        var iconCenterX = GetLeftEdge((Control)button, icon) + icon.Bounds.Width / 2;
        var buttonCenterY = GetTopEdge((Control)button, (Control)button) + button.Bounds.Height / 2;
        var iconCenterY = GetTopEdge((Control)button, icon) + icon.Bounds.Height / 2;
        if (Math.Abs(buttonCenterX - iconCenterX) > 1.5 ||
            Math.Abs(buttonCenterY - iconCenterY) > 1.5)
        {
            throw new InvalidOperationException(
                $"{button.GetType().Name}:{AutomationProperties.GetAutomationId((Control)button)} " +
                $"remove icon should be centered: buttonCenter=({buttonCenterX:F1},{buttonCenterY:F1}); " +
                $"iconCenter=({iconCenterX:F1},{iconCenterY:F1}).");
        }
    }

    private static Border FindTextBoxTemplateBorder(TextBox textBox)
    {
        return textBox.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(static border =>
                string.Equals(border.Name, "PART_BorderElement", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"{textBox.GetType().Name}:{AutomationProperties.GetAutomationId(textBox)} template border was not found.");
    }

    private static void AssertBorderlessTextBoxChrome(TextBox textBox, string source)
    {
        var templateBorder = FindTextBoxTemplateBorder(textBox);

        if (textBox.BorderThickness != new Thickness(0))
        {
            throw new InvalidOperationException(
                $"{source} text box border thickness should be 0, got {textBox.BorderThickness}.");
        }

        AssertTransparentBrush(textBox.BorderBrush, $"{source} text box border");
        AssertTransparentBrush(textBox.Background, $"{source} text box background");

        if (templateBorder.BorderThickness != new Thickness(0))
        {
            throw new InvalidOperationException(
                $"{source} template border thickness should be 0, got {templateBorder.BorderThickness}.");
        }

        AssertTransparentBrush(templateBorder.BorderBrush, $"{source} template border");
        AssertTransparentBrush(templateBorder.Background, $"{source} template background");
    }

    private static void AssertTaskHeaderAlignment(Control header)
    {
        var statusPicker = FindControlByAutomationId<Control>(header, "CurrentTaskStatusButton");
        var wantedCheckBox = FindControlByAutomationId<CheckBox>(header, "CurrentTaskWantedCheckBox");
        var titleTextBox = FindControlByAutomationId<TextBox>(header, "CurrentTaskTitleTextBox");
        var titlePresenter = titleTextBox.GetVisualDescendants()
            .OfType<TextPresenter>()
            .FirstOrDefault(IsVisibleAndArranged)
            ?? throw new InvalidOperationException("Current task title text presenter was not found.");
        var wantedText = wantedCheckBox.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(IsVisibleAndArranged)
            ?? throw new InvalidOperationException("Current task wanted label was not found.");

        AssertAligned(
            "Current task status picker and wanted checkbox indicator",
            GetLeftEdge(header, statusPicker),
            GetLeftEdge(header, wantedCheckBox));
        AssertAligned(
            "Current task title content and wanted label",
            GetLeftEdge(header, titlePresenter),
            GetLeftEdge(header, wantedText));
    }

    private static void AssertAligned(string subject, double actual, double expected)
    {
        const double AlignmentTolerance = 1;
        if (Math.Abs(actual - expected) > AlignmentTolerance)
        {
            throw new InvalidOperationException(
                $"{subject} should share one vertical guide, got {actual:F1} and {expected:F1}.");
        }
    }

    private static void AssertTransparentBrush(IBrush? brush, string source)
    {
        if (brush is not ISolidColorBrush solidColorBrush ||
            solidColorBrush.Color != Colors.Transparent)
        {
            throw new InvalidOperationException($"{source} should be transparent, got {brush?.ToString() ?? "null"}.");
        }
    }

    private static void AssertDoesNotUseLightThemeAccentBackground(Control control)
    {
        var lightAccentBackground = Color.Parse("#F7FAFF");
        var lightBackgroundUsages = GetBackgroundColors(control)
            .Where(color => color == lightAccentBackground)
            .ToArray();

        if (lightBackgroundUsages.Length > 0)
        {
            throw new InvalidOperationException(
                $"{control.GetType().Name}:{AutomationProperties.GetAutomationId(control)} " +
                $"uses the light-theme accent background in dark theme.");
        }
    }

    private static IEnumerable<Color> GetBackgroundColors(Control control)
    {
        return new[] { control }
            .Concat(control.GetVisualDescendants().OfType<Control>())
            .Select(GetBackground)
            .Where(static brush => brush is not null)
            .Select(brush => GetSolidBrushColor(brush!));
    }

    private static IBrush? GetBackground(Control control)
    {
        return control switch
        {
            Border border => border.Background,
            DropDownButton dropDownButton => dropDownButton.Background,
            Button button => button.Background,
            _ => null
        };
    }

    private static Color GetSolidBrushColor(IBrush brush)
    {
        if (brush is ISolidColorBrush solidColorBrush)
        {
            return solidColorBrush.Color;
        }

        return Colors.Transparent;
    }

    private static void AssertCreateMenuContainsTaskCommands(DropDownButton createMenuButton)
    {
        if (createMenuButton.Flyout is not MenuFlyout menuFlyout)
        {
            throw new InvalidOperationException("Create menu button should use a MenuFlyout.");
        }

        var itemAutomationIds = menuFlyout.Items
            .OfType<MenuItem>()
            .Select(AutomationProperties.GetAutomationId)
            .ToHashSet(StringComparer.Ordinal);

        string[] expectedAutomationIds =
        [
            "GlobalTaskCreateTaskMenuItem",
            "GlobalTaskCreateSiblingMenuItem",
            "GlobalTaskCreateBlockedSiblingMenuItem",
            "GlobalTaskCreateInnerMenuItem"
        ];

        foreach (var automationId in expectedAutomationIds)
        {
            if (!itemAutomationIds.Contains(automationId))
            {
                throw new InvalidOperationException(
                    $"Create menu is missing expected item '{automationId}'.");
            }
        }
    }

    private static void AssertCreateMenuUsesTouchFriendlyItems(DropDownButton createMenuButton)
    {
        if (createMenuButton.Flyout is not MenuFlyout menuFlyout)
        {
            throw new InvalidOperationException("Create menu button should use a MenuFlyout.");
        }

        menuFlyout.ShowAt(createMenuButton);
        RunLayoutJobs();

        try
        {
            foreach (var item in menuFlyout.Items.OfType<MenuItem>())
            {
                var automationId = AutomationProperties.GetAutomationId(item);
                if (automationId is null ||
                    !automationId.StartsWith("GlobalTaskCreate", StringComparison.Ordinal))
                {
                    continue;
                }

                if (item.Bounds.Height < 44)
                {
                    throw new InvalidOperationException(
                        $"Create menu item '{automationId}' should have at least a 44px touch target: " +
                        $"height={item.Bounds.Height:F1}; bounds={item.Bounds}.");
                }
            }
        }
        finally
        {
            menuFlyout.Hide();
            RunLayoutJobs();
        }
    }

    private static void AssertActionsMenuContainsTaskCommands(DropDownButton actionsMenuButton)
    {
        if (actionsMenuButton.Flyout is not MenuFlyout menuFlyout)
        {
            throw new InvalidOperationException("Task actions button should use a MenuFlyout.");
        }

        var itemAutomationIds = menuFlyout.Items
            .OfType<MenuItem>()
            .Select(AutomationProperties.GetAutomationId)
            .ToHashSet(StringComparer.Ordinal);

        string[] expectedAutomationIds =
        [
            "CurrentTaskMoveToPathMenuItem",
            "CurrentTaskArchiveMenuItem",
            "CurrentTaskRemoveMenuItem"
        ];

        foreach (var automationId in expectedAutomationIds)
        {
            if (!itemAutomationIds.Contains(automationId))
            {
                throw new InvalidOperationException(
                    $"Task actions menu is missing expected item '{automationId}'.");
            }
        }
    }

    private static void AssertVisibleAndArranged(Control control, string automationId)
    {
        if (!IsVisibleAndArranged(control))
        {
            throw new InvalidOperationException(
                $"{control.GetType().Name}:{automationId} is not visible and arranged: " +
                $"visible={control.IsVisible}; bounds={control.Bounds}.");
        }
    }

    private static bool IsFocused(Window? window, Control control)
    {
        return ReferenceEquals(window?.FocusManager?.GetFocusedElement(), control) || control.IsFocused;
    }

    private static void AssertTaskActionsMenuSitsAfterIdBelowTitle(Control root)
    {
        var title = FindControlByAutomationId<Control>(root, "CurrentTaskTitleTextBox");
        var idText = FindControlByAutomationId<Control>(root, "CurrentTaskIdTextBlock");
        var actionsMenuButton = FindControlByAutomationId<Control>(root, "CurrentTaskActionsMenuButton");

        var titleBottom = GetBottomEdge(root, title);
        var idRight = GetRightEdge(root, idText);
        var actionsTop = GetTopEdge(root, actionsMenuButton);
        var actionsLeft = GetLeftEdge(root, actionsMenuButton);

        if (actionsTop < titleBottom - 1)
        {
            throw new InvalidOperationException(
                $"Task actions menu should sit below the title row: " +
                $"titleBottom={titleBottom:F1}; actionsTop={actionsTop:F1}.");
        }

        if (actionsLeft <= idRight)
        {
            throw new InvalidOperationException(
                $"Task actions menu should sit to the right of the task identifier: " +
                $"idRight={idRight:F1}; actionsLeft={actionsLeft:F1}.");
        }
    }

    private static void AssertStatusHistoryLivesAtTaskCardBottomAndExpandsDown(Control root)
    {
        var statusHistorySection = FindControlByAutomationId<Control>(root, "CurrentTaskStatusHistorySection");
        var statusHistoryExpander = FindControlByAutomationId<Expander>(statusHistorySection, "StatusHistoryExpander");
        var statusHistoryToggle = statusHistoryExpander.GetVisualDescendants()
            .OfType<ToggleButton>()
            .FirstOrDefault(IsVisibleAndArranged)
            ?? throw new InvalidOperationException("Status history expander toggle was not found.");
        var statusHistoryTop = GetTopEdge(root, statusHistorySection);
        var misplacedSection = SectionAutomationIds
            .Where(static automationId =>
                !string.Equals(automationId, "CurrentTaskCard", StringComparison.Ordinal) &&
                !string.Equals(automationId, "CurrentTaskStatusHistorySection", StringComparison.Ordinal))
            .Select(automationId => new
            {
                AutomationId = automationId,
                Top = GetTopEdge(root, FindControlByAutomationId<Control>(root, automationId))
            })
            .FirstOrDefault(section => section.Top > statusHistoryTop + 1);

        if (misplacedSection is not null)
        {
            throw new InvalidOperationException(
                $"Status history should be the bottom task card section, but " +
                $"{misplacedSection.AutomationId} starts below it: " +
                $"statusTop={statusHistoryTop:F1}; sectionTop={misplacedSection.Top:F1}.");
        }

        if (statusHistoryExpander.ExpandDirection != ExpandDirection.Down)
        {
            throw new InvalidOperationException(
                $"Status history expander should open down, got {statusHistoryExpander.ExpandDirection}.");
        }

        if (string.Equals(statusHistoryToggle.Content?.ToString(), "<", StringComparison.Ordinal) ||
            statusHistoryToggle.Bounds.Width < statusHistorySection.Bounds.Width / 2)
        {
            throw new InvalidOperationException(
                "Status history expander should render as a horizontal bottom section header, " +
                $"not as the old side toggle: content={statusHistoryToggle.Content}; " +
                $"toggleBounds={statusHistoryToggle.Bounds}; sectionBounds={statusHistorySection.Bounds}.");
        }
    }

    private static void AssertDesktopPlanningGroupsStayCompactRow(Control root)
    {
        var planningControls = PlanningControlAutomationIds
            .Select(automationId => FindControlByAutomationId<Control>(root, automationId))
            .ToArray();

        var topEdges = planningControls
            .Select(control => GetTopEdge(root, control))
            .ToArray();
        var bottomEdges = planningControls
            .Select(control => GetBottomEdge(root, control))
            .ToArray();
        var widths = planningControls
            .Select(control => control.Bounds.Width)
            .ToArray();
        var heights = planningControls
            .Select(control => control.Bounds.Height)
            .ToArray();

        var beginTop = topEdges[0];
        var durationTop = topEdges[2];
        var endTop = topEdges[4];
        if (Math.Abs(beginTop - durationTop) > 2 || Math.Abs(beginTop - endTop) > 2)
        {
            throw new InvalidOperationException(
                "Desktop planning fields should stay in one compact row: " +
                $"beginTop={beginTop:F1}; durationTop={durationTop:F1}; endTop={endTop:F1}. " +
                DescribeTaskDetailsLayout(root));
        }

        var maxPlanningControlWidth = widths.Max();
        if (maxPlanningControlWidth > 260)
        {
            throw new InvalidOperationException(
                "Desktop planning controls are too wide for a compact three-column row: " +
                string.Join("; ", PlanningControlAutomationIds.Zip(widths, (id, width) => $"{id}={width:F1}")));
        }

        var firstRowTop = topEdges[0];
        var wrappedPlanningControl = PlanningControlAutomationIds
            .Zip(topEdges, (automationId, top) => new { AutomationId = automationId, Top = top })
            .FirstOrDefault(item => Math.Abs(item.Top - firstRowTop) > 2);
        if (wrappedPlanningControl is not null)
        {
            throw new InvalidOperationException(
                "Desktop planning groups should fit into one row: " +
                $"firstTop={firstRowTop:F1}; wrapped={wrappedPlanningControl.AutomationId}; " +
                $"top={wrappedPlanningControl.Top:F1}.");
        }

        var leftEdges = planningControls
            .Select(control => GetLeftEdge(root, control))
            .ToArray();
        var rightEdges = planningControls
            .Select(control => GetRightEdge(root, control))
            .ToArray();

        var beginActionTop = topEdges[1];
        var durationActionTop = topEdges[3];
        var endActionTop = topEdges[5];
        if (Math.Abs(beginActionTop - topEdges[0]) > 2 ||
            Math.Abs(durationActionTop - topEdges[2]) > 2 ||
            Math.Abs(endActionTop - topEdges[4]) > 2)
        {
            throw new InvalidOperationException("Desktop planning quick actions should align with matching value controls.");
        }

        if (leftEdges[1] <= rightEdges[0] ||
            leftEdges[3] <= rightEdges[2] ||
            leftEdges[5] <= rightEdges[4])
        {
            throw new InvalidOperationException("Desktop planning quick actions should sit to the right of their matching value controls.");
        }

        var beginPickerHeight = heights[0];
        var durationHeight = heights[2];
        var endPickerHeight = heights[4];
        if (Math.Abs(beginPickerHeight - durationHeight) > 1 || Math.Abs(beginPickerHeight - endPickerHeight) > 1)
        {
            throw new InvalidOperationException(
                "Desktop planning value controls should have matching heights: " +
                $"begin={beginPickerHeight:F1}; duration={durationHeight:F1}; end={endPickerHeight:F1}.");
        }

        var planningSection = FindControlByAutomationId<Control>(root, "CurrentTaskPlanningSection");
        AssertRowUsesRightEdge(planningSection, planningControls, 20d, "Desktop planning row");
    }

    private static void AssertDesktopRepeaterControlsStayCompact(Control root, bool requireWeekdayToggles = false)
    {
        var repeaterSelector = FindControlByAutomationId<ComboBox>(root, "CurrentTaskRepeaterSelector");
        if (repeaterSelector.Bounds.Width > 300)
        {
            throw new InvalidOperationException(
                $"Desktop repeater selector is too wide: width={repeaterSelector.Bounds.Width:F1}.");
        }

        AssertDesktopRepeaterPatternControlsStayInlineWhenVisible(root, repeaterSelector);

        var weekdayToggles = root.GetVisualDescendants()
            .OfType<ToggleButton>()
            .Where(static toggle => toggle.Classes.Contains("WeekdayToggle"))
            .Where(IsVisibleAndArranged)
            .ToArray();

        if (requireWeekdayToggles && weekdayToggles.Length != 7)
        {
            throw new InvalidOperationException(
                $"Expected seven visible desktop weekday toggles, found {weekdayToggles.Length}.");
        }

        if (requireWeekdayToggles)
        {
            var weekdayPanel = root.GetVisualDescendants()
                .OfType<WrapPanel>()
                .FirstOrDefault(panel =>
                    panel.Classes.Contains("WeekdayToggles") &&
                    IsVisibleAndArranged(panel))
                ?? throw new InvalidOperationException("Visible weekday toggle panel was not found.");
            var firstTop = GetTopEdge(root, weekdayToggles[0]);
            var wrappedToggle = weekdayToggles
                .Select(toggle => new { Toggle = toggle, Top = GetTopEdge(root, toggle) })
                .FirstOrDefault(item => Math.Abs(item.Top - firstTop) > 2);
            if (wrappedToggle is not null)
            {
                throw new InvalidOperationException(
                    $"Desktop weekday toggles should stay in one compact row: " +
                    $"content={wrappedToggle.Toggle.Content}; firstTop={firstTop:F1}; top={wrappedToggle.Top:F1}; " +
                    $"panelBounds={weekdayPanel.Bounds}; " +
                    $"toggleBounds={string.Join(", ", weekdayToggles.Select(toggle => $"{toggle.Content}:{toggle.Bounds}"))}. " +
                    DescribeTaskDetailsLayout(root));
            }

            AssertRowUsesRightEdge(weekdayPanel, weekdayToggles, 8d, "Desktop weekday toggle row");
        }

        foreach (var toggle in weekdayToggles)
        {
            if (toggle.Bounds.Width > 64 || toggle.Bounds.Height > 36)
            {
                throw new InvalidOperationException(
                    $"Desktop weekday toggle is too large: content={toggle.Content}; bounds={toggle.Bounds}.");
            }
        }
    }

    private static void AssertDesktopRepeaterPatternControlsStayInlineWhenVisible(
        Control root,
        ComboBox repeaterSelector)
    {
        var patternTypeSelector = root.GetVisualDescendants()
            .OfType<ComboBox>()
            .FirstOrDefault(comboBox =>
                string.Equals(
                    AutomationProperties.GetAutomationId(comboBox),
                    "CurrentTaskRepeaterPatternTypeSelector",
                    StringComparison.Ordinal) &&
                IsVisibleAndArranged(comboBox));

        if (patternTypeSelector is null)
        {
            return;
        }

        var periodInput = FindControlByAutomationId<NumericUpDown>(root, "CurrentTaskRepeaterPeriodInput");
        var afterCompleteCheckBox = FindControlByAutomationId<CheckBox>(root, "CurrentTaskRepeaterAfterCompleteCheckBox");
        var visibleRepeaterControls = new Control[]
        {
            repeaterSelector,
            patternTypeSelector,
            periodInput,
            afterCompleteCheckBox
        };

        foreach (var control in visibleRepeaterControls)
        {
            if (!IsVisibleAndArranged(control))
            {
                throw new InvalidOperationException(
                    $"{AutomationProperties.GetAutomationId(control)} should be visible in the desktop repeater row: " +
                    $"visible={control.IsVisible}; bounds={control.Bounds}.");
            }
        }

        var firstRowTop = GetTopEdge(root, repeaterSelector);
        var wrappedRepeaterControl = visibleRepeaterControls
            .Select(control => new
            {
                Control = control,
                Top = GetTopEdge(root, control)
            })
            .FirstOrDefault(item => Math.Abs(item.Top - firstRowTop) > 2);
        if (wrappedRepeaterControl is not null)
        {
            throw new InvalidOperationException(
                "Desktop repeater selector and pattern controls should fit into one row: " +
                $"control={AutomationProperties.GetAutomationId(wrappedRepeaterControl.Control)}; " +
                $"firstTop={firstRowTop:F1}; top={wrappedRepeaterControl.Top:F1}.");
        }

        var selectorRight = GetRightEdge(root, repeaterSelector);
        var patternTypeLeft = GetLeftEdge(root, patternTypeSelector);
        var patternTypeRight = GetRightEdge(root, patternTypeSelector);
        var periodLeft = GetLeftEdge(root, periodInput);
        var periodRight = GetRightEdge(root, periodInput);
        var afterCompleteLeft = GetLeftEdge(root, afterCompleteCheckBox);

        if (patternTypeLeft <= selectorRight || periodLeft <= patternTypeRight || afterCompleteLeft <= periodRight)
        {
            throw new InvalidOperationException(
                "Desktop repeater pattern controls should sit to the right of the previous control: " +
                $"selectorRight={selectorRight:F1}; typeLeft={patternTypeLeft:F1}; " +
                $"typeRight={patternTypeRight:F1}; periodLeft={periodLeft:F1}; " +
                $"periodRight={periodRight:F1}; afterLeft={afterCompleteLeft:F1}.");
        }

        var repeaterSection = FindControlByAutomationId<Control>(root, "CurrentTaskRepeaterSection");
        AssertRowUsesRightEdge(repeaterSection, visibleRepeaterControls, 8d, "Desktop repeater row");
    }

    private static void AssertRepeaterControlsDoNotOverlap(Control root)
    {
        var repeaterSection = FindControlByAutomationId<Control>(root, "CurrentTaskRepeaterSection");
        var repeaterControls = new List<Control>
        {
            FindControlByAutomationId<ComboBox>(root, "CurrentTaskRepeaterSelector"),
            FindControlByAutomationId<ComboBox>(root, "CurrentTaskRepeaterPatternTypeSelector"),
            FindControlByAutomationId<NumericUpDown>(root, "CurrentTaskRepeaterPeriodInput"),
            FindControlByAutomationId<CheckBox>(root, "CurrentTaskRepeaterAfterCompleteCheckBox")
        };
        repeaterControls.AddRange(root.GetVisualDescendants()
            .OfType<ToggleButton>()
            .Where(static toggle => toggle.Classes.Contains("WeekdayToggle")));

        var visibleControls = repeaterControls
            .Where(IsVisibleAndArranged)
            .ToArray();

        foreach (var control in visibleControls)
        {
            AssertHorizontallyContained(repeaterSection, control);
        }

        for (var i = 0; i < visibleControls.Length; i++)
        {
            for (var j = i + 1; j < visibleControls.Length; j++)
            {
                var first = visibleControls[i];
                var second = visibleControls[j];
                var verticalOverlap =
                    GetTopEdge(repeaterSection, first) < GetBottomEdge(repeaterSection, second) - 1 &&
                    GetTopEdge(repeaterSection, second) < GetBottomEdge(repeaterSection, first) - 1;
                var horizontalOverlap =
                    GetLeftEdge(repeaterSection, first) < GetRightEdge(repeaterSection, second) - 1 &&
                    GetLeftEdge(repeaterSection, second) < GetRightEdge(repeaterSection, first) - 1;

                if (verticalOverlap && horizontalOverlap)
                {
                    throw new InvalidOperationException(
                        "Repeater controls should not overlap: " +
                        $"{AutomationProperties.GetAutomationId(first)} bounds={first.Bounds}; " +
                        $"{AutomationProperties.GetAutomationId(second)} bounds={second.Bounds}.");
                }
            }
        }
    }

    private static void AssertRowUsesRightEdge(
        Control rowContainer,
        IReadOnlyCollection<Control> rowControls,
        double maxTrailingGap,
        string rowName)
    {
        var rightEdge = rowControls.Max(control => GetRightEdge(rowContainer, control));
        var trailingGap = rowContainer.Bounds.Width - rightEdge;
        if (trailingGap > maxTrailingGap)
        {
            throw new InvalidOperationException(
                $"{rowName} leaves too much unused space on the right: " +
                $"gap={trailingGap:F1}; allowed={maxTrailingGap:F1}; container={rowContainer.Bounds.Width:F1}.");
        }
    }

    private static void AssertNoHorizontalOverflow(Visual relativeTo, Control root)
    {
        var overflowingControls = root.GetVisualDescendants()
            .OfType<Control>()
            .Where(IsVisibleAndArranged)
            .Where(control => !IsTemplatePartInsideInputControl(control))
            .Select(control => new
            {
                Control = control,
                RightEdge = GetRightEdge(relativeTo, control)
            })
            .Where(item => item.RightEdge > ((Control)relativeTo).Bounds.Width + 1)
            .Select(item =>
                $"{item.Control.GetType().Name}:{item.Control.Name} " +
                $"right={item.RightEdge:F1} width={item.Control.Bounds.Width:F1}")
            .ToList();

        if (overflowingControls.Count > 0)
        {
            var viewport = (Control)relativeTo;
            throw new InvalidOperationException(
                $"Visible task card controls overflow the phone-width details pane. Viewport={viewport.Bounds}: " +
                string.Join("; ", overflowingControls));
        }
    }

    private static void AssertHorizontallyContained(Visual relativeTo, Control control)
    {
        var leftEdge = GetLeftEdge(relativeTo, control);
        var rightEdge = GetRightEdge(relativeTo, control);
        var viewportWidth = ((Control)relativeTo).Bounds.Width;

        if (leftEdge < -1 || rightEdge > viewportWidth + 1)
        {
            throw new InvalidOperationException(
                $"{control.GetType().Name}:{AutomationProperties.GetAutomationId(control)} is not fully contained " +
                $"in the phone-width details pane: left={leftEdge:F1}; right={rightEdge:F1}; viewport={viewportWidth:F1}.");
        }
    }

    private static bool IsTemplatePartInsideInputControl(Control control)
    {
        if (control is TextBox or ComboBox or NumericUpDown or CalendarDatePicker or DropDownButton)
        {
            return false;
        }

        return control.GetVisualAncestors()
            .Any(ancestor => ancestor is TextBox or ComboBox or NumericUpDown or CalendarDatePicker or DropDownButton);
    }

    private static double GetTop(Control control, Visual relativeTo) => control.TranslatePoint(default, relativeTo)!.Value.Y;

    private static bool IsVisibleAndArranged(Control control)
    {
        return control.IsEffectivelyVisible &&
               control.Bounds.Width > 0 &&
               control.Bounds.Height > 0;
    }

    private static double GetRightEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width, 0), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return point.Value.X;
    }

    private static double GetLeftEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(0, 0), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate left edge for control {control.GetType().Name}.");
        }

        return point.Value.X;
    }

    private static double GetTopEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(0, 0), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate top edge for control {control.GetType().Name}.");
        }

        return point.Value.Y;
    }

    private static double GetBottomEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(0, control.Bounds.Height), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate bottom edge for control {control.GetType().Name}.");
        }

        return point.Value.Y;
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void CloseWindow(Window? window)
    {
        if (window == null)
        {
            return;
        }

        window.Content = null;
        RunLayoutJobs();
        window.Close();
        RunLayoutJobs();
    }

    private static string DescribeTaskDetailsLayout(Control root)
    {
        var splitView = root.GetVisualDescendants().OfType<SplitView>().FirstOrDefault();
        var scrollViewer = root.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault(static control =>
                string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    "CurrentTaskDetailsScrollViewer",
                    StringComparison.Ordinal));
        var panel = root.GetVisualDescendants()
            .OfType<Grid>()
            .FirstOrDefault(static control => string.Equals(control.Name, "TaskDetailsPanelRoot", StringComparison.Ordinal));
        var paneLength = splitView == null ? "null" : splitView.OpenPaneLength.ToString("F1", CultureInfo.InvariantCulture);
        var isCompact = panel?.Classes.Contains("TaskDetailsCompact");

        return "Layout: " +
               $"root={root.Bounds}; " +
               $"split={splitView?.Bounds}; open={splitView?.IsPaneOpen}; pane={paneLength}; " +
               $"scroll={scrollViewer?.Bounds}; " +
               $"panel={panel?.Bounds}; compact={isCompact}.";
    }

    private sealed class FakeSystemCultureProvider : ILocalizationSystemCultureProvider
    {
        public FakeSystemCultureProvider(string cultureName)
        {
            SystemUICulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public CultureInfo SystemUICulture { get; }
    }
}

public sealed class RenderedTaskHistoryAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithCustomFont();
}
