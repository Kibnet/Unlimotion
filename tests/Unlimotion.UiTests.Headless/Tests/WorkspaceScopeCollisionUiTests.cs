using System.Reflection;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class WorkspaceScopeCollisionUiTests
    : UiTestBase<MainWindowHeadlessTests.HeadlessRuntimeSession, MainWindowPage>
{
    protected override MainWindowHeadlessTests.HeadlessRuntimeSession LaunchSession() => new(
        DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
            UnlimotionAutomationScenario.TaskSpaces)));

    protected override MainWindowPage CreatePage(MainWindowHeadlessTests.HeadlessRuntimeSession session)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            session.Inner.MainWindow.Show();
            Dispatcher.UIThread.RunJobs();
        });
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task DelayedOldFeedCallback_WithCollidingTaskAndNoteIds_CannotMutateNewSpace()
    {
        var owner = HeadlessRuntime.Dispatch(() => (MainWindowViewModel)Session.Inner.MainWindow.DataContext!);
        HeadlessRuntime.Dispatch(() =>
        {
            owner.Feed.IsExternalVaultSupported = true;
            owner.Feed.TaskOwner = owner;
            owner.Feed.TaskResolver = id => owner.ResolveTaskById(id);
            _ = owner.Feed.InitializeVaultAsync(owner.Settings.NoteVaultRootPath);
        });
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.IsVaultInitialized && owner.Feed.Days.Count == 1),
            ready => ready, timeout: TimeSpan.FromSeconds(40), timeoutMessage: "Space A vault did not initialize.");
        var taskA = HeadlessRuntime.Dispatch(() => owner.ResolveTaskById(UnlimotionAutomationScenarioData.TaskSpacesTaskId)!);
        var dayA = HeadlessRuntime.Dispatch(() => owner.Feed.Days.Single());
        var path = dayA.RelativePath;
        var rootA = HeadlessRuntime.Dispatch(() => owner.Feed.VaultRootPath);
        await RunUiAsync(() => owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForFeedDay(path, "Space A day")));
        await RunUiAsync(() => owner.OpenWorkspaceTaskAsync(taskA, WorkspaceOpenDisposition.AdjacentPane));
        await RunUiAsync(() => owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForFeedDay(path, "Space A day")));
        var staleArgs = HeadlessRuntime.Dispatch(() => new FeedSearchNavigationRequestedEventArgs(
            path, dayA.MarkdownEditor,
            dayA.MarkdownEditor.Blocks.Single(block => block.PreviewText.Contains("Space A note", StringComparison.Ordinal)).Index,
            dayA, "Space A note"));

        // A's payload is queued, not A's view delegates: delayed VM delivery must
        // use the current real subscribers after the selector has entered B.
        var navigationEventField = typeof(FeedViewModel)
            .GetField(nameof(FeedViewModel.SearchNavigationRequested), BindingFlags.Instance | BindingFlags.NonPublic)!;
        var releaseOldCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task DeliverOldCallbackAsync()
        {
            await releaseOldCallback.Task;
            await HeadlessRuntime.Session.Dispatch(() =>
            {
                var subscribers = (EventHandler<FeedSearchNavigationRequestedEventArgs>?)navigationEventField.GetValue(owner.Feed)
                    ?? throw new InvalidOperationException("Space B has no real feed navigation subscriber.");
                subscribers(owner.Feed, staleArgs);
                return Task.CompletedTask;
            }, CancellationToken.None).ContinueWith(task => task.GetAwaiter().GetResult(),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        var delayedDelivery = DeliverOldCallbackAsync();

        try
        {
            var selector = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "TaskSpaceSelector"));
            HeadlessRuntime.Dispatch(() => selector.SelectedItem = owner.Settings.TaskSpaces.Single(space => space.DisplayName == "Space B"));
            WaitUntil(() => HeadlessRuntime.Dispatch(() =>
                !owner.Settings.IsTaskSpaceSwitching && owner.Settings.TaskSpaces.Single(space => space.DisplayName == "Space B").IsActive &&
                owner.Feed.IsVaultInitialized && owner.Feed.Days.Count == 1 &&
                owner.Feed.Days.Single().MarkdownEditor.Blocks.Any(block => block.PreviewText.Contains("Space B note", StringComparison.Ordinal))),
                ready => ready, timeout: TimeSpan.FromSeconds(40), timeoutMessage: "Real selector did not activate Space B tasks and notes.");
            var taskB = HeadlessRuntime.Dispatch(() => owner.ResolveTaskById(taskA.Id)!);
            var dayB = HeadlessRuntime.Dispatch(() => owner.Feed.Days.Single());
            await Assert.That(taskB.Id).IsEqualTo(taskA.Id);
            await Assert.That(taskB).IsNotSameReferenceAs(taskA);
            await Assert.That(taskB.Title).IsEqualTo(UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle);
            await Assert.That(dayB.RelativePath).IsEqualTo(path);
            await Assert.That(dayB).IsNotSameReferenceAs(dayA);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.Feed.VaultRootPath)).IsNotEqualTo(rootA);

            await RunUiAsync(() => owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForFeedDay(path, "Space B day")));
            await RunUiAsync(() => owner.OpenWorkspaceTaskAsync(taskB, WorkspaceOpenDisposition.AdjacentPane));
            await RunUiAsync(() => owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForFeedDay(path, "Space B day")));
            HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Session.Inner.MainWindow.UpdateLayout();
            });
            var feed = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<FeedControl>()
                .Single(control => control.IsEffectivelyVisible));
            var search = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<TextBox>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "GlobalSearchBox"));
            HeadlessRuntime.Dispatch(() => { search.Focus(); search.CaretIndex = 0; });
            var before = HeadlessRuntime.Dispatch(() => new
            {
                Tab = owner.WorkspaceNavigation.ActiveTab,
                Index = owner.WorkspaceNavigation.ActiveTab.CurrentIndex,
                History = owner.WorkspaceNavigation.ActiveTab.History.Select(entry => entry.Location.HistoryKey).ToArray(),
                Scope = owner.WorkspaceNavigation.ScopeRevision,
                Route = owner.WorkspaceNavigation.ActiveTab.CurrentLocation,
                FilterAll = feed.WorkspaceFilterAll,
                Areas = feed.WorkspaceSelectedAreas.ToArray(),
                SelectedDay = owner.Feed.SelectedDay,
                Caret = dayB.MarkdownEditor.LastCaretPosition,
                Text = search.Text,
                SearchCaret = search.CaretIndex
            });

            releaseOldCallback.TrySetResult();
            await delayedDelivery.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Session.Inner.MainWindow.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            });

            await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab)).IsSameReferenceAs(before.Tab);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentIndex)).IsEqualTo(before.Index);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation)).IsEqualTo(before.Route);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.History.Select(entry => entry.Location.HistoryKey)
                .SequenceEqual(before.History))).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ScopeRevision)).IsEqualTo(before.Scope);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.Feed.SelectedDay)).IsSameReferenceAs(before.SelectedDay);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.Feed.SelectedDay)).IsNotSameReferenceAs(dayA);
            await Assert.That(HeadlessRuntime.Dispatch(() => feed.WorkspaceFilterAll)).IsEqualTo(before.FilterAll);
            await Assert.That(HeadlessRuntime.Dispatch(() => feed.WorkspaceSelectedAreas.SequenceEqual(before.Areas))).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => dayB.MarkdownEditor.LastCaretPosition)).IsEqualTo(before.Caret);
            await Assert.That(HeadlessRuntime.Dispatch(() => search.Text)).IsEqualTo(before.Text);
            await Assert.That(HeadlessRuntime.Dispatch(() => search.CaretIndex)).IsEqualTo(before.SearchCaret);
            await Assert.That(HeadlessRuntime.Dispatch(() => search.IsFocused)).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => feed.DisplayDays!.All(day => !ReferenceEquals(day, dayA)))).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => feed.GetVisualDescendants().OfType<TextBlock>()
                .Any(control => control.IsEffectivelyVisible && control.Text?.Contains("Space A note", StringComparison.Ordinal) == true))).IsFalse();
            var card = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<TaskCardView>().Single());
            await Assert.That(HeadlessRuntime.Dispatch(() => card.RouteTaskItem)).IsSameReferenceAs(taskB);
            await Assert.That(HeadlessRuntime.Dispatch(() => card.RouteTaskItem!.Title)).IsEqualTo(UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle);
        }
        finally
        {
            releaseOldCallback.TrySetResult();
            await delayedDelivery.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        }
    }

    private static Task RunUiAsync(Func<Task<bool>> action) =>
        HeadlessRuntime.Session.Dispatch(action, CancellationToken.None).ContinueWith(task => task.GetAwaiter().GetResult(),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
}
