using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class TaskGoalRemovalUiTests
{
    [Test]
    [Arguments("Goals", 1600)]
    [Arguments("Regular", 1600)]
    [Arguments("Goals", 700)]
    [Arguments("Regular", 700)]
    public async Task LegacyGoalFilterDoesNotHideTasks_AndAllListsAndCardsHaveNoGoalControls(string legacyMode, int width)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture(null, legacyGoalFilter: legacyMode);
            var owner = fixture.MainWindowViewModelTest;
            await owner.Connect();
            owner.AllTasksMode = true;
            var shell = new MainScreen { DataContext = owner };
            var window = new Window { Width = width, Height = 850, Content = shell };
            try
            {
                window.Show();
                foreach (var kind in Enum.GetValues<TaskListKind>())
                {
                    await Assert.That(await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(kind))).IsTrue();
                    Pump(window);
                    var controls = shell.GetVisualDescendants().OfType<Control>().ToArray();
                    await Assert.That(controls.Any(c => AutomationProperties.GetAutomationId(c)?.Contains("Goal") == true)).IsFalse();
                    foreach (var button in controls.OfType<DropDownButton>().Where(b => b.IsEffectivelyVisible).ToArray())
                    {
                        if (button.Flyout is not Flyout flyout || flyout.Content is not Control content) continue;
                        flyout.ShowAt(button);
                        Pump(window);
                        await Assert.That(content.GetVisualDescendants().OfType<Control>()
                            .Any(c => AutomationProperties.GetAutomationId(c)?.Contains("Goal") == true)).IsFalse();
                        flyout.Hide();
                    }
                }
                await Assert.That(await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.TasksRoot)).IsTrue();
                await Assert.That(await TestHelpers.WaitUntilAsync(() =>
                {
                    Pump(window);
                    return owner.CurrentAllTasksItems.Count >= 2;
                }, TimeSpan.FromSeconds(5))).IsTrue();
                var task = owner.ResolveTaskById(MainWindowViewModelFixture.RootTask2Id)!;
                await Assert.That(await owner.OpenWorkspaceTaskAsync(task, WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                Pump(window);
                var card = shell.GetVisualDescendants().OfType<TaskCardView>().Single();
                await Assert.That(card.GetVisualDescendants().OfType<Control>()
                    .Any(c => AutomationProperties.GetAutomationId(c)?.Contains("Goal") == true)).IsFalse();
                await Assert.That(card.GetVisualDescendants().OfType<TaskClassificationControl>().Any()).IsTrue();
                var folder = Path.Combine(AppContext.BaseDirectory, "chat-artifacts", "remove-isgoal");
                Directory.CreateDirectory(folder);
                using var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
                bitmap.Save(Path.Combine(folder, $"no-goal-{legacyMode}-{width}.png"));
            }
            finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
        }, CancellationToken.None);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
