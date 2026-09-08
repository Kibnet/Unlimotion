using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class TaskRelationsControlUiTests
{
    [Test]
    public async Task CurrentTaskCard_ReusableParentsControl_AddsParentWithExistingAutomationIds()
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
                owner.AllTasksMode = true;
                owner.DetailsAreOpen = true;
                var target = TestHelpers.SetCurrentTask(owner, MainWindowViewModelFixture.BlockedTask7Id);
                await Assert.That(target).IsNotNull();

                var view = new MainControl { DataContext = owner };
                window = CreateWindow(view, 1600, 2200);
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();

                var relations = WaitForControl<TaskRelationsControl>(view);
                using (Assert.Multiple())
                {
                    await Assert.That(relations.Owner).IsSameReferenceAs(owner);
                    await Assert.That(relations.TargetTask).IsSameReferenceAs(target);
                    await Assert.That(relations.OpenParentTaskOnDoubleTap).IsTrue();
                }

                await AddParentAsync(
                    view,
                    owner,
                    TaskRelationsControl.CurrentTaskAutomationIdPrefix,
                    MainWindowViewModelFixture.RootTask1Id,
                    "Root Task 1");

                var relationStored = WaitFor(() =>
                    TestHelpers.GetStorageTaskItem(
                        fixture.DefaultTasksFolderPath,
                        MainWindowViewModelFixture.BlockedTask7Id)?.ParentTasks.Contains(
                        MainWindowViewModelFixture.RootTask1Id) == true,
                    10000);

                await Assert.That(relationStored).IsTrue();
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ExplicitNonCurrentTarget_AddsAndRemovesMultipleParents_WithoutChangingCurrentTask()
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
                var target = TestHelpers.SetCurrentTask(owner, MainWindowViewModelFixture.BlockedTask7Id);
                var globalCurrent = TestHelpers.SetCurrentTask(owner, MainWindowViewModelFixture.RootTask7Id);
                await Assert.That(target).IsNotNull();
                await Assert.That(globalCurrent).IsNotNull();

                var relations = new TaskRelationsControl
                {
                    Owner = owner,
                    TargetTask = target,
                    AutomationIdPrefix = TaskRelationsControl.FeedAutomationIdPrefix,
                    ParentsTreeAutomationId = TaskRelationsControl.FeedParentsTreeAutomationId
                };
                window = CreateWindow(relations, 900, 1200);
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();

                await AddParentAsync(
                    relations,
                    owner,
                    TaskRelationsControl.FeedAutomationIdPrefix,
                    MainWindowViewModelFixture.RootTask1Id,
                    "Root Task 1");
                await AddParentAsync(
                    relations,
                    owner,
                    TaskRelationsControl.FeedAutomationIdPrefix,
                    MainWindowViewModelFixture.RootTask2Id,
                    "Task 2");

                var multipleParentsStored = WaitFor(() =>
                {
                    var stored = TestHelpers.GetStorageTaskItem(
                        fixture.DefaultTasksFolderPath,
                        MainWindowViewModelFixture.BlockedTask7Id);
                    return stored?.ParentTasks.Contains(MainWindowViewModelFixture.RootTask1Id) == true &&
                           stored.ParentTasks.Contains(MainWindowViewModelFixture.RootTask2Id);
                }, 10000);
                var multipleParentsProjected = WaitFor(() =>
                    relations.ParentsRoot?.SubTasks.Count == 2 &&
                    relations.ParentsRoot.SubTasks.Any(parent =>
                        parent.Id == MainWindowViewModelFixture.RootTask1Id) &&
                    relations.ParentsRoot.SubTasks.Any(parent =>
                        parent.Id == MainWindowViewModelFixture.RootTask2Id),
                    5000);

                using (Assert.Multiple())
                {
                    await Assert.That(multipleParentsStored).IsTrue();
                    await Assert.That(multipleParentsProjected).IsTrue();
                    await Assert.That(owner.CurrentTaskItem).IsSameReferenceAs(globalCurrent);
                    await Assert.That(relations.OpenParentTaskOnDoubleTap).IsFalse();
                }

                var rootOneWrapper = relations.ParentsRoot!.SubTasks
                    .First(parent => parent.Id == MainWindowViewModelFixture.RootTask1Id);
                var removeButton = WaitForControl<Button>(
                    relations,
                    "ParentRelationRemoveButton",
                    button => ReferenceEquals(button.DataContext, rootOneWrapper));
                RaiseClick(removeButton);
                await TestHelpers.WaitThrottleTime();
                Dispatcher.UIThread.RunJobs();

                var relationRemoved = WaitFor(() =>
                {
                    var stored = TestHelpers.GetStorageTaskItem(
                        fixture.DefaultTasksFolderPath,
                        MainWindowViewModelFixture.BlockedTask7Id);
                    var formerParent = TestHelpers.GetStorageTaskItem(
                        fixture.DefaultTasksFolderPath,
                        MainWindowViewModelFixture.RootTask1Id);
                    return stored != null &&
                           formerParent != null &&
                           !stored.ParentTasks.Contains(MainWindowViewModelFixture.RootTask1Id) &&
                           stored.ParentTasks.Contains(MainWindowViewModelFixture.RootTask2Id) &&
                           !formerParent.ContainsTasks.Contains(MainWindowViewModelFixture.BlockedTask7Id) &&
                           relations.ParentsRoot?.SubTasks.Count == 1 &&
                           relations.ParentsRoot.SubTasks.Single().Id == MainWindowViewModelFixture.RootTask2Id;
                }, 10000);

                using (Assert.Multiple())
                {
                    await Assert.That(relationRemoved).IsTrue();
                    await Assert.That(owner.CurrentTaskItem).IsSameReferenceAs(globalCurrent);
                }
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static async Task AddParentAsync(
        Control root,
        MainWindowViewModel owner,
        string automationIdPrefix,
        string parentTaskId,
        string searchText)
    {
        var addButton = WaitForControl<Button>(root, $"{automationIdPrefix}AddButton");
        RaiseClick(addButton);

        var input = WaitForControl<TextBox>(root, $"{automationIdPrefix}AddInput");
        input.Text = searchText;
        Dispatcher.UIThread.RunJobs();

        var candidateReady = WaitFor(() =>
            owner.CurrentRelationEditor.CanConfirm &&
            owner.CurrentRelationEditor.Suggestions.Any(candidate => candidate.Task.Id == parentTaskId),
            5000);
        await Assert.That(candidateReady).IsTrue();

        owner.CurrentRelationEditor.SelectedCandidate = owner.CurrentRelationEditor.Suggestions
            .First(candidate => candidate.Task.Id == parentTaskId);
        Dispatcher.UIThread.RunJobs();

        var confirmButton = WaitForControl<Button>(root, $"{automationIdPrefix}AddConfirmButton");
        confirmButton.Command?.Execute(confirmButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        await TestHelpers.WaitThrottleTime();
        Dispatcher.UIThread.RunJobs();
    }

    private static Window CreateWindow(Control content, double width, double height)
    {
        return new Window
        {
            Width = width,
            Height = height,
            Content = content
        };
    }

    private static void RaiseClick(Button button)
    {
        if (button.Command is { } command && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }
        else
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static T WaitForControl<T>(
        Control root,
        string? automationId = null,
        Func<T, bool>? predicate = null,
        int timeoutMilliseconds = 3000)
        where T : Control
    {
        T? control = null;
        var ready = WaitFor(() =>
        {
            control = root.GetVisualDescendants()
                .OfType<T>()
                .FirstOrDefault(candidate =>
                    (automationId == null || string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        automationId,
                        StringComparison.Ordinal)) &&
                    (predicate == null || predicate(candidate)) &&
                    candidate.IsAttachedToVisualTree() &&
                    candidate.IsEffectivelyVisible &&
                    candidate.IsEnabled);
            return control != null;
        }, timeoutMilliseconds);

        if (!ready || control == null)
        {
            throw new InvalidOperationException(
                automationId == null
                    ? $"Control of type {typeof(T).Name} was not found."
                    : $"Control with AutomationId '{automationId}' was not found.");
        }

        return control;
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 3000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }
}
