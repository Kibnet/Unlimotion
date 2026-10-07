using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class WorkspaceTaskLifetimeUiTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeletedOpenTask_RemovesStaleEditorAndKeepsContextualHistory(bool deleteFromAdjacentPane)
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
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Content = shell, Width = 1280, Height = 800 };
                window.Show();
                var task = owner.ResolveTaskById(MainWindowViewModelFixture.RootTask1Id)!;
                await Assert.That(await owner.OpenWorkspaceTaskAsync(task)).IsTrue();
                Dispatcher.UIThread.RunJobs();
                var cardPane = owner.WorkspaceNavigation.PrimaryPane;
                var cardTab = cardPane.ActiveTab!;
                var originalCard = shell.GetVisualDescendants().OfType<TaskCardView>().Single();
                await Assert.That(await owner.OpenWorkspaceTaskAsync(task)).IsTrue();
                await Assert.That(shell.GetVisualDescendants().OfType<TaskCardView>().Single()).IsSameReferenceAs(originalCard);
                if (deleteFromAdjacentPane)
                    await Assert.That(await owner.OpenWorkspaceLocationAsync(
                        WorkspaceLocation.ForTaskList(TaskListKind.InProgress), WorkspaceOpenDisposition.AdjacentPane)).IsTrue();

                await Assert.That(await owner.taskRepository!.Delete(task)).IsTrue();
                await Assert.That(await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return !shell.GetVisualDescendants().OfType<TaskCardView>().Any();
                }, System.TimeSpan.FromSeconds(3))).IsTrue();
                await Assert.That(originalCard.Parent).IsNull();
                await Assert.That(cardTab.CurrentLocation!.Id).IsEqualTo(task.Id);
                var unavailable = shell.GetVisualDescendants().OfType<TextBlock>().Single(control =>
                    control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "WorkspaceTaskUnavailable");
                await Assert.That(unavailable.Text!).Contains(task.Id);
                await Assert.That(cardTab.CanGoBack).IsTrue();
                await Assert.That(await owner.NavigateWorkspaceBackAsync(cardPane)).IsTrue();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(cardTab.CurrentLocation!.Kind).IsEqualTo(WorkspaceLocationKind.Tasks);
                await owner.NavigateWorkspaceForwardAsync(cardPane);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(cardTab.CurrentLocation!.Id).IsEqualTo(task.Id);
                await Assert.That(shell.GetVisualDescendants().OfType<TaskCardView>().Any()).IsFalse();
                await Assert.That(shell.GetVisualDescendants().OfType<TextBlock>().Any(control =>
                    control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "WorkspaceTaskUnavailable")).IsTrue();
            }
            finally
            {
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskToFeed_ReleasesMountedCardAndBackRestoresItsSnapshot()
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
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Content = shell, Width = 1280, Height = 800 };
                window.Show();
                var task = owner.ResolveTaskById(MainWindowViewModelFixture.RootTask1Id)!;
                await Assert.That(await owner.OpenWorkspaceTaskAsync(task)).IsTrue();
                Dispatcher.UIThread.RunJobs();
                var card = shell.GetVisualDescendants().OfType<TaskCardView>().Single();
                var taskEntry = owner.WorkspaceNavigation.ActiveTab.CurrentEntry!;
                await Assert.That(await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.FeedRoot)).IsTrue();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(taskEntry.ViewState).IsNotNull();
                await Assert.That(card.Parent).IsNull();
                await Assert.That(shell.GetVisualDescendants().OfType<TaskCardView>().Any()).IsFalse();
                await Assert.That(await owner.NavigateWorkspaceBackAsync()).IsTrue();
                Dispatcher.UIThread.RunJobs();
                var restored = shell.GetVisualDescendants().OfType<TaskCardView>().Single();
                await Assert.That(restored.RouteTaskItem).IsSameReferenceAs(task);
                await Assert.That(ReferenceEquals(card, restored)).IsFalse();
            }
            finally
            {
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }
}
