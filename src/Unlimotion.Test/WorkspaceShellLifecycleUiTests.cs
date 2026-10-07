using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class WorkspaceShellLifecycleUiTests
{
    [Test]
    public async Task DetachingShell_ReleasesDocumentCallbacksAndReattachesExistingHistory()
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
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(TaskListKind.Unlocked));
                owner.UnlockedSearch.SearchText = "lifecycle filter";
                Dispatcher.UIThread.RunJobs();
                var tab = owner.WorkspaceNavigation.ActiveTab;
                var index = tab.CurrentIndex;
                var history = tab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                await Assert.That(owner.CaptureWorkspaceTabState).IsNotNull();
                window.Content = null;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(owner.IsWorkspaceShellAttached).IsFalse();
                await Assert.That(owner.CaptureWorkspaceTabState).IsNull();
                await Assert.That(owner.RestoreWorkspaceTabState).IsNull();
                // Assert the delegate itself, not TUnit's Func<T> execution overload.
                await Assert.That((object?)owner.CaptureActiveFeedLocation).IsNull();
                await Assert.That(tab.CurrentEntry!.ViewState).IsNotNull();
                await Assert.That(shell.GetVisualDescendants().OfType<TaskPresentationControl>()).IsEmpty();
                window.Content = shell;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Assert.That(owner.IsWorkspaceShellAttached).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.ActiveTab.Id).IsEqualTo(tab.Id);
                await Assert.That(tab.CurrentIndex).IsEqualTo(index);
                await Assert.That(tab.History.Select(entry => entry.Location.HistoryKey).ToArray()).IsEquivalentTo(history);
                await Assert.That(owner.UnlockedSearch.SearchText).IsEqualTo("lifecycle filter");
                await Assert.That(shell.GetVisualDescendants().OfType<TaskListDocumentView>()
                    .Single(view => view.IsEffectivelyVisible).Kind).IsEqualTo(TaskListKind.Unlocked);
            }
            finally
            {
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }
}
