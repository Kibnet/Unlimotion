using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using Unlimotion;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using Unlimotion.Views.Graph;

namespace Unlimotion.Test;

[NotInParallel]
public class RoadmapGraphUiTests
{
    [Test]
    public async Task RoadmapGraphProjection_BuildsNodesAndTypedConnections()
    {
        var fixture = new MainWindowViewModelFixture();

        try
        {
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            vm.GraphMode = true;

            var graphReady = SpinWait.SpinUntil(
                () => vm.Graph.Tasks.Count > 0,
                TimeSpan.FromSeconds(3));
            await Assert.That(graphReady).IsTrue();

            var projection = RoadmapGraphBuilder.Build(vm.Graph.Tasks);

            await Assert.That(projection.Nodes.Any(node => node.Id == MainWindowViewModelFixture.RootTask2Id)).IsTrue();
            await Assert.That(projection.Nodes.Any(node => node.Id == MainWindowViewModelFixture.SubTask22Id)).IsTrue();
            await Assert.That(projection.Connections.Any(connection =>
                connection.Kind == RoadmapConnectionKind.Contains &&
                connection.Tail.Id == MainWindowViewModelFixture.SubTask22Id &&
                connection.Head.Id == MainWindowViewModelFixture.RootTask2Id)).IsTrue();
            await Assert.That(projection.Connections.Any(connection =>
                connection.Kind == RoadmapConnectionKind.Blocks)).IsTrue();
            await Assert.That(projection.Connections.All(connection => connection.Tail.Id != connection.Head.Id)).IsTrue();
            await Assert.That(projection.Connections.All(connection => connection.Tail.Location.X < connection.Head.Location.X)).IsTrue();
            await Assert.That(projection.Connections.All(connection => connection.IsLeftToRight)).IsTrue();

            var containsConnection = projection.Connections.First(connection =>
                connection.Kind == RoadmapConnectionKind.Contains &&
                connection.Tail.Id == MainWindowViewModelFixture.SubTask22Id &&
                connection.Head.Id == MainWindowViewModelFixture.RootTask2Id);
            var blockConnection = projection.Connections.First(connection =>
                connection.Kind == RoadmapConnectionKind.Blocks &&
                connection.Tail.Id == MainWindowViewModelFixture.RootTask2Id &&
                connection.Head.Id == MainWindowViewModelFixture.BlockedTask2Id);
            var shortTitleNode = projection.Nodes.First(node => node.Id == MainWindowViewModelFixture.RootTask2Id);

            await Assert.That(containsConnection.Head.Location.X).IsGreaterThan(containsConnection.Tail.Location.X);
            await Assert.That(blockConnection.Head.Location.X).IsGreaterThan(blockConnection.Tail.Location.X);
            await Assert.That(shortTitleNode.Width).IsLessThan(200);
            await Assert.That(shortTitleNode.RightAnchor.X).IsEqualTo(shortTitleNode.Location.X + shortTitleNode.Width);
        }
        finally
        {
            fixture.CleanTasks();
        }
    }

    [Test]
    public async Task RoadmapGraphProjection_OrdersLayersToReduceConnectionCrossings()
    {
        var storage = new StubTaskStorage();
        var sourceA = CreateTask("source-a", "Source A", storage);
        var sourceB = CreateTask("source-b", "Source B", storage);
        var targetA = CreateTask("target-a", "Target A", storage);
        var targetB = CreateTask("target-b", "Target B", storage);

        sourceA.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { targetA },
            Array.Empty<TaskItemViewModel>());
        sourceB.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { targetB },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(sourceA, sourceB, targetB, targetA));
        var sourceAToTargetA = projection.Connections.Single(connection => connection.Tail == projection.Nodes.Single(node => node.TaskItem == sourceA) &&
                                                                           connection.Head == projection.Nodes.Single(node => node.TaskItem == targetA));
        var sourceBToTargetB = projection.Connections.Single(connection => connection.Tail == projection.Nodes.Single(node => node.TaskItem == sourceB) &&
                                                                           connection.Head == projection.Nodes.Single(node => node.TaskItem == targetB));

        await Assert.That(sourceAToTargetA.Tail.Location.Y).IsLessThan(sourceBToTargetB.Tail.Location.Y);
        await Assert.That(sourceAToTargetA.Head.Location.Y).IsLessThan(sourceBToTargetB.Head.Location.Y);
        await Assert.That(CountConnectionCrossings(projection.Connections)).IsEqualTo(0);
    }

    [Test]
    public async Task RoadmapGraphProjection_KeepsLongBlockConnectionsMostlyHorizontal()
    {
        var storage = new StubTaskStorage();
        var target = CreateTask("target", "Add private bool IsEmpty", storage);
        var fillerA = CreateTask("filler-a", "SortedSetComparable", storage);
        var fillerB = CreateTask("filler-b", "IsFullSet", storage);
        var fillerC = CreateTask("filler-c", "Document IntSet ordering", storage);
        var blocker = CreateTask("blocker", "123", storage);

        blocker.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { target },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(
            target,
            fillerA,
            fillerB,
            fillerC,
            blocker));
        var blockConnection = projection.Connections.Single(connection =>
            connection.Kind == RoadmapConnectionKind.Blocks &&
            connection.Tail.TaskItem == blocker &&
            connection.Head.TaskItem == target);

        await Assert.That(blockConnection.Head.Location.X).IsGreaterThan(blockConnection.Tail.Location.X);
        await Assert.That(Math.Abs(blockConnection.Source.Y - blockConnection.Target.Y)).IsLessThan(92);
    }

    [Test]
    public async Task RoadmapGraphProjection_CompactsLayersToShortenContainConnections()
    {
        var storage = new StubTaskStorage();
        var parent = CreateTask("parent", "Parent", storage);
        var child = CreateTask("child", "Child", storage);
        var grandchild = CreateTask("grandchild", "Grandchild", storage);
        var sibling = CreateTask("sibling", "Sibling", storage);

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappersFromWrappers(
            CreateWrapper(
                parent,
                CreateWrapper(child, CreateWrapper(grandchild)),
                CreateWrapper(sibling))));
        var childToParent = projection.Connections.Single(connection =>
            connection.Kind == RoadmapConnectionKind.Contains &&
            connection.Tail.TaskItem == child &&
            connection.Head.TaskItem == parent);
        var siblingToParent = projection.Connections.Single(connection =>
            connection.Kind == RoadmapConnectionKind.Contains &&
            connection.Tail.TaskItem == sibling &&
            connection.Head.TaskItem == parent);

        await Assert.That(childToParent.Head.Location.X - childToParent.Tail.Location.X).IsEqualTo(520);
        await Assert.That(siblingToParent.Head.Location.X - siblingToParent.Tail.Location.X).IsEqualTo(520);
        await Assert.That(CountConnectionCrossings(projection.Connections)).IsEqualTo(0);
    }

    [Test]
    public async Task RoadmapGraph_NodifyView_RendersTasksAndKeepsAutomationIds()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = WaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var nodesReady = WaitFor(() => graphControl!.RoadmapNodes.Count > 0);
                await Assert.That(nodesReady).IsTrue();
                await Assert.That(graphControl!.RoadmapConnections.Any(connection =>
                    connection.Kind == RoadmapConnectionKind.Contains)).IsTrue();
                await Assert.That(graphControl.RoadmapConnections.Any(connection =>
                    connection.Kind == RoadmapConnectionKind.Blocks)).IsTrue();

                var roadmapZoomTarget = view.GetVisualDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(control =>
                        AutomationProperties.GetAutomationId(control) == "RoadmapZoomBorder");

                await Assert.That(roadmapZoomTarget).IsNotNull();
                await Assert.That(roadmapZoomTarget!.GetType().Name).IsEqualTo("NodifyEditor");

                var graphText = WaitForTaskTitleTextBlock(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id);

                await Assert.That(graphText).IsNotNull();

                var completionCheckBox = WaitForTaskCompletionCheckBox(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id);
                var rootTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var rootNode = graphControl.RoadmapNodes.First(node => node.Id == MainWindowViewModelFixture.RootTask2Id);

                await Assert.That(completionCheckBox.IsChecked).IsEqualTo(rootTask!.IsCompleted);
                await Assert.That(completionCheckBox.IsEnabled).IsEqualTo(rootTask.IsCanBeCompleted);
                await Assert.That(taskNodeWidth(graphText)).IsEqualTo(rootNode.Width);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = WaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var nodesReady = WaitFor(() => graphControl!.RoadmapNodes.Count > 0);
                await Assert.That(nodesReady).IsTrue();

                var editor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                var minimap = WaitForAutomationControl<Control>(view, "RoadmapMinimap");
                var toolbar = WaitForAutomationControl<Control>(view, "RoadmapViewportToolbar");
                var zoomInButton = WaitForAutomationControl<Button>(view, "RoadmapZoomInButton");
                var panRightButton = WaitForAutomationControl<Button>(view, "RoadmapPanRightButton");
                var resetButton = WaitForAutomationControl<Button>(view, "RoadmapResetViewportButton");

                await Assert.That(editor.GetType().Name).IsEqualTo("NodifyEditor");
                await Assert.That(minimap.GetType().Name).IsEqualTo("Minimap");
                await Assert.That(toolbar).IsNotNull();

                var zoomProperty = editor.GetType().GetProperty("ViewportZoom")!;
                var locationProperty = editor.GetType().GetProperty("ViewportLocation")!;
                var initialZoom = (double)zoomProperty.GetValue(editor)!;

                zoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var zoomed = WaitFor(() => (double)zoomProperty.GetValue(editor)! > initialZoom);
                await Assert.That(zoomed).IsTrue();

                var initialLocation = (Avalonia.Point)locationProperty.GetValue(editor)!;
                panRightButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var panned = WaitFor(() =>
                    ((Avalonia.Point)locationProperty.GetValue(editor)!).X > initialLocation.X);
                await Assert.That(panned).IsTrue();

                resetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var reset = WaitFor(() =>
                {
                    var zoom = (double)zoomProperty.GetValue(editor)!;
                    var location = (Avalonia.Point)locationProperty.GetValue(editor)!;

                    return Math.Abs(zoom - 1) < 0.001 &&
                           Math.Abs(location.X) < 0.001 &&
                           Math.Abs(location.Y) < 0.001;
                });
                await Assert.That(reset).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_NodeWidthFollowsRenderedTitleAfterRename()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = WaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var originalNode = FindRoadmapNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(originalNode).IsNotNull();

                var rootTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(rootTask).IsNotNull();

                rootTask!.Title = "1231233445345343534534534534534535345";
                var wideReady = WaitFor(() =>
                {
                    var text = FindTaskTitleTextBlock(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                    var node = FindRoadmapNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                    return text?.Text == rootTask.TitleWithoutEmoji &&
                           node?.Width > 300 &&
                           Math.Abs(taskNodeWidth(text) - node.Width) < 0.5;
                });
                await Assert.That(wideReady).IsTrue();
                await Assert.That(ReferenceEquals(
                    originalNode,
                    FindRoadmapNode(graphControl!, MainWindowViewModelFixture.RootTask2Id))).IsTrue();

                var wideWidth = FindRoadmapNode(graphControl!, MainWindowViewModelFixture.RootTask2Id)!.Width;

                rootTask.Title = "A";
                var narrowReady = WaitFor(() =>
                {
                    var text = FindTaskTitleTextBlock(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                    var node = FindRoadmapNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                    return text?.Text == rootTask.TitleWithoutEmoji &&
                           node != null &&
                           node.Width < wideWidth - 150 &&
                           node.Width <= RoadmapNode.MinWidth + 1 &&
                           Math.Abs(taskNodeWidth(text) - node.Width) < 0.5 &&
                           Math.Abs(node.RightAnchor.X - (node.Location.X + node.Width)) < 0.5;
                });
                await Assert.That(narrowReady).IsTrue();
                await Assert.That(ReferenceEquals(
                    originalNode,
                    FindRoadmapNode(graphControl!, MainWindowViewModelFixture.RootTask2Id))).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_OpenView_ReflectsCreatedAndDeletedTasks()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = WaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(parent).IsNotNull();

                var newTask = await vm.taskRepository!.AddChild(parent!);
                newTask.Title = "Roadmap live child";

                var appeared = WaitFor(() =>
                    graphControl!.RoadmapNodes.Any(node => node.Id == newTask.Id) &&
                    FindTaskTitleTextBlock(graphControl!, newTask.Id)?.Text == newTask.TitleWithoutEmoji);
                await Assert.That(appeared).IsTrue();

                await vm.taskRepository.Delete(newTask);

                var disappeared = WaitFor(() =>
                    graphControl!.RoadmapNodes.All(node => node.Id != newTask.Id) &&
                    FindTaskTitleTextBlock(graphControl!, newTask.Id) == null);
                await Assert.That(disappeared).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_FilterChange_RebuildsOpenView()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = WaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var filterReady = WaitFor(() =>
                    vm.Graph.EmojiExcludeFilters.Any(filter =>
                        !string.IsNullOrEmpty(filter.Emoji) &&
                        filter.Source != null &&
                        FindRoadmapNode(graphControl!, filter.Source.Id) != null));
                await Assert.That(filterReady).IsTrue();

                var excludeFilter = vm.Graph.EmojiExcludeFilters.First(filter =>
                    !string.IsNullOrEmpty(filter.Emoji) &&
                    filter.Source != null &&
                    FindRoadmapNode(graphControl!, filter.Source.Id) != null);
                var excludedTaskId = excludeFilter.Source.Id;

                excludeFilter.ShowTasks = true;
                var filteredOut = WaitFor(() =>
                    FindRoadmapNode(graphControl!, excludedTaskId) == null &&
                    FindTaskTitleTextBlock(graphControl!, excludedTaskId) == null);
                await Assert.That(filteredOut).IsTrue();

                excludeFilter.ShowTasks = false;
                var restored = WaitFor(() =>
                    FindRoadmapNode(graphControl!, excludedTaskId) != null &&
                    FindTaskTitleTextBlock(graphControl!, excludedTaskId) != null);
                await Assert.That(restored).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_NodeDoubleTap_TogglesDetailsPanel()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;
                vm.DetailsAreOpen = false;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = WaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var taskNode = WaitForTaskNode(
                    graphControl!,
                    MainWindowViewModelFixture.RootTask2Id);

                taskNode.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent));

                await Assert.That(vm.DetailsAreOpen).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;

                var targetTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(targetTask).IsNotNull();
                targetTask!.Wanted = false;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = WaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var graphText = WaitForTaskTitleTextBlock(
                    graphControl!,
                    MainWindowViewModelFixture.RootTask2Id);

                vm.Graph.Search.SearchText = targetTask.OnlyTextTitle;
                var highlighted = WaitFor(() => targetTask.IsHighlighted);

                await Assert.That(highlighted).IsTrue();
                await Assert.That(graphText.FontWeight).IsEqualTo(FontWeight.Bold);
                await Assert.That(graphText.Foreground is ISolidColorBrush brush && brush.Color == Color.Parse("#2F80ED"))
                    .IsTrue();

                vm.Graph.Search.SearchText = "";
                var cleared = WaitFor(() => !targetTask.IsHighlighted);

                await Assert.That(cleared).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1400,
            Height = 900,
            Content = content
        };
    }

    private static GraphControl? WaitForGraphControl(Control root, int timeoutMilliseconds = 3000)
    {
        GraphControl? graphControl = null;
        var ready = WaitFor(() =>
        {
            graphControl = root.GetVisualDescendants().OfType<GraphControl>().FirstOrDefault();
            return graphControl != null;
        }, timeoutMilliseconds);

        return ready ? graphControl : null;
    }

    private static T WaitForAutomationControl<T>(
        Control root,
        string automationId,
        int timeoutMilliseconds = 3000)
        where T : Control
    {
        T? control = null;
        var ready = WaitFor(() =>
        {
            control = root.GetVisualDescendants()
                .OfType<T>()
                .FirstOrDefault(candidate =>
                    AutomationProperties.GetAutomationId(candidate) == automationId);

            return control != null;
        }, timeoutMilliseconds);

        if (!ready || control == null)
        {
            throw new InvalidOperationException($"Control with automation id '{automationId}' was not found.");
        }

        return control;
    }

    private static TextBlock? FindTaskTitleTextBlock(Control root, string taskId)
    {
        return root.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(candidate =>
                candidate.DataContext is TaskItemViewModel task &&
                task.Id == taskId &&
                candidate.Text == task.TitleWithoutEmoji);
    }

    private static TextBlock WaitForTaskTitleTextBlock(
        Control root,
        string taskId,
        int timeoutMilliseconds = 3000)
    {
        TextBlock? textBlock = null;
        var ready = WaitFor(() =>
        {
            textBlock = FindTaskTitleTextBlock(root, taskId);
            return textBlock != null;
        }, timeoutMilliseconds);

        if (!ready || textBlock == null)
        {
            throw new InvalidOperationException($"Roadmap title TextBlock for task '{taskId}' was not found.");
        }

        return textBlock;
    }

    private static RoadmapNode? FindRoadmapNode(GraphControl graphControl, string taskId)
    {
        return graphControl.RoadmapNodes.FirstOrDefault(node => node.Id == taskId);
    }

    private static Border WaitForTaskNode(
        Control root,
        string taskId,
        int timeoutMilliseconds = 3000)
    {
        Border? border = null;
        var ready = WaitFor(() =>
        {
            border = root.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(candidate =>
                    candidate.DataContext is RoadmapNode node &&
                    node.Id == taskId);

            return border != null;
        }, timeoutMilliseconds);

        if (!ready || border == null)
        {
            throw new InvalidOperationException($"Roadmap node for task '{taskId}' was not found.");
        }

        return border;
    }

    private static double taskNodeWidth(Control taskChild)
    {
        var border = taskChild.GetVisualAncestors().OfType<Border>().First();
        return border.Bounds.Width;
    }

    private static CheckBox WaitForTaskCompletionCheckBox(
        Control root,
        string taskId,
        int timeoutMilliseconds = 3000)
    {
        CheckBox? checkBox = null;
        var ready = WaitFor(() =>
        {
            checkBox = root.GetVisualDescendants()
                .OfType<CheckBox>()
                .FirstOrDefault(candidate =>
                    candidate.DataContext is TaskItemViewModel task &&
                    task.Id == taskId);

            return checkBox != null;
        }, timeoutMilliseconds);

        if (!ready || checkBox == null)
        {
            throw new InvalidOperationException($"Roadmap completion CheckBox for task '{taskId}' was not found.");
        }

        return checkBox;
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 3000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private static TaskItemViewModel CreateTask(string id, string title, ITaskStorage storage)
    {
        return new TaskItemViewModel(
            new TaskItem
            {
                Id = id,
                Title = title,
                BlocksTasks = new List<string>(),
                BlockedByTasks = new List<string>(),
                ContainsTasks = new List<string>(),
                ParentTasks = new List<string>(),
            },
            storage,
            () => false);
    }

    private static ReadOnlyObservableCollection<TaskWrapperViewModel> CreateRootWrappers(
        params TaskItemViewModel[] tasks)
    {
        var wrappers = new ObservableCollection<TaskWrapperViewModel>(
            tasks.Select(task =>
            {
                var wrapper = new TaskWrapperViewModel(null!, task, new TaskWrapperActions());
                wrapper.SubTasks = new ReadOnlyObservableCollection<TaskWrapperViewModel>(
                    new ObservableCollection<TaskWrapperViewModel>());
                return wrapper;
            }));

        return new ReadOnlyObservableCollection<TaskWrapperViewModel>(wrappers);
    }

    private static ReadOnlyObservableCollection<TaskWrapperViewModel> CreateRootWrappersFromWrappers(
        params TaskWrapperViewModel[] wrappers)
    {
        return new ReadOnlyObservableCollection<TaskWrapperViewModel>(
            new ObservableCollection<TaskWrapperViewModel>(wrappers));
    }

    private static TaskWrapperViewModel CreateWrapper(
        TaskItemViewModel task,
        params TaskWrapperViewModel[] children)
    {
        var wrapper = new TaskWrapperViewModel(null!, task, new TaskWrapperActions());
        var childCollection = new ObservableCollection<TaskWrapperViewModel>(children);

        foreach (var child in children)
        {
            child.Parent = wrapper;
        }

        wrapper.SubTasks = new ReadOnlyObservableCollection<TaskWrapperViewModel>(childCollection);
        return wrapper;
    }

    private static int CountConnectionCrossings(IEnumerable<RoadmapConnection> connections)
    {
        var orderedConnections = connections.ToList();
        var crossings = 0;

        for (var i = 0; i < orderedConnections.Count; i++)
        {
            for (var j = i + 1; j < orderedConnections.Count; j++)
            {
                var first = orderedConnections[i];
                var second = orderedConnections[j];
                var sourceOrder = first.Source.Y.CompareTo(second.Source.Y);
                var targetOrder = first.Target.Y.CompareTo(second.Target.Y);

                if (sourceOrder != 0 && targetOrder != 0 && sourceOrder != targetOrder)
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    private sealed class StubTaskStorage : ITaskStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);

        public ITaskRelationsIndex Relations { get; } = new TaskRelationsIndex();

        public TaskTreeManager TaskTreeManager => throw new NotSupportedException();

        public event EventHandler<EventArgs>? Initiated
        {
            add { }
            remove { }
        }

        public Task Init() => Task.CompletedTask;

        public Task<TaskItemViewModel> Add(TaskItemViewModel? currentTask = null, bool isBlocked = false) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Update(TaskItemViewModel change) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Update(TaskItem change) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) =>
            throw new NotSupportedException();

        public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) =>
            throw new NotSupportedException();
    }
}
