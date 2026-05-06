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
    public async Task RoadmapGraphProjection_AlignsOutgoingRoutesByMaxNodeWidth()
    {
        var storage = new StubTaskStorage();
        var shortSource = CreateTask("short-source", "Short", storage);
        var longSource = CreateTask(
            "long-source",
            "Long source title that reaches the roadmap node maximum width",
            storage);
        var shortTarget = CreateTask("short-target", "Short target", storage);
        var longTarget = CreateTask("long-target", "Long target", storage);

        shortSource.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { shortTarget },
            Array.Empty<TaskItemViewModel>());
        longSource.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { longTarget },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(
            CreateRootWrappers(shortSource, longSource, shortTarget, longTarget),
            new Dictionary<string, double>
            {
                [shortSource.Id] = RoadmapNode.MinWidth,
                [longSource.Id] = RoadmapNode.MaxWidth
            });

        var shortConnection = projection.Connections.Single(connection => connection.Tail.Id == shortSource.Id);
        var longConnection = projection.Connections.Single(connection => connection.Tail.Id == longSource.Id);

        await Assert.That(shortConnection.Tail.Width).IsLessThan(longConnection.Tail.Width);
        await Assert.That(shortConnection.Source.X).IsEqualTo(shortConnection.Tail.RightAnchor.X);
        await Assert.That(shortConnection.RoutedSource.X).IsGreaterThan(shortConnection.Source.X);
        await Assert.That(longConnection.RoutedSource.X).IsEqualTo(longConnection.Source.X);
        await Assert.That(shortConnection.HasSourceExtension).IsTrue();
        await Assert.That(longConnection.HasSourceExtension).IsFalse();
        await Assert.That(Math.Abs(shortConnection.RoutedSource.X - longConnection.RoutedSource.X)).IsLessThan(0.5);
        await Assert.That(Math.Abs(
            shortConnection.RoutedSource.X -
            (shortConnection.Tail.Location.X + RoadmapNode.MaxWidth))).IsLessThan(0.5);
    }

    [Test]
    public async Task RoadmapGraphProjection_OrdersLayersToReduceConnectionCrossings()
    {
        var storage = new StubTaskStorage();
        var sourceA = CreateTask("source-a", "Source A", storage);
        var sourceB = CreateTask("source-b", "Source B", storage);
        var sourceC = CreateTask("source-c", "Source C", storage);
        var targetA = CreateTask("target-a", "Target A", storage);
        var targetB = CreateTask("target-b", "Target B", storage);
        var targetC = CreateTask("target-c", "Target C", storage);

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
        sourceC.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { targetC },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(
            sourceA,
            sourceB,
            sourceC,
            targetC,
            targetB,
            targetA));
        var sourceAToTargetA = projection.Connections.Single(connection => connection.Tail == projection.Nodes.Single(node => node.TaskItem == sourceA) &&
                                                                           connection.Head == projection.Nodes.Single(node => node.TaskItem == targetA));
        var sourceBToTargetB = projection.Connections.Single(connection => connection.Tail == projection.Nodes.Single(node => node.TaskItem == sourceB) &&
                                                                           connection.Head == projection.Nodes.Single(node => node.TaskItem == targetB));
        var sourceCToTargetC = projection.Connections.Single(connection => connection.Tail == projection.Nodes.Single(node => node.TaskItem == sourceC) &&
                                                                           connection.Head == projection.Nodes.Single(node => node.TaskItem == targetC));

        await Assert.That(sourceAToTargetA.IsLeftToRight).IsTrue();
        await Assert.That(sourceBToTargetB.IsLeftToRight).IsTrue();
        await Assert.That(sourceCToTargetC.IsLeftToRight).IsTrue();
        await Assert.That(CountConnectionCrossings(projection.Connections)).IsEqualTo(0);
    }

    [Test]
    public async Task RoadmapGraphProjection_AnchorsColumnsFromGoalsInsteadOfSources()
    {
        var storage = new StubTaskStorage();
        var longStart = CreateTask("long-start", "Long start", storage);
        var longMiddle = CreateTask("long-middle", "Long middle", storage);
        var longEnd = CreateTask("long-end", "Long end", storage);
        var sharedGoal = CreateTask("shared-goal", "Shared goal", storage);
        var shortStart = CreateTask("short-start", "Short start", storage);
        var shortGoal = CreateTask("short-goal", "Short goal", storage);

        longStart.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { longMiddle },
            Array.Empty<TaskItemViewModel>());
        longMiddle.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { longEnd },
            Array.Empty<TaskItemViewModel>());
        longEnd.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { sharedGoal },
            Array.Empty<TaskItemViewModel>());
        shortStart.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { shortGoal },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(
            longStart,
            longMiddle,
            longEnd,
            sharedGoal,
            shortStart,
            shortGoal));
        var longStartNode = projection.Nodes.Single(node => node.TaskItem == longStart);
        var shortStartNode = projection.Nodes.Single(node => node.TaskItem == shortStart);
        var sharedGoalNode = projection.Nodes.Single(node => node.TaskItem == sharedGoal);
        var shortGoalNode = projection.Nodes.Single(node => node.TaskItem == shortGoal);

        await Assert.That(projection.Connections.All(connection => connection.IsLeftToRight)).IsTrue();
        await Assert.That(shortStartNode.Location.X).IsGreaterThan(longStartNode.Location.X);
        await Assert.That(shortGoalNode.Location.X).IsEqualTo(sharedGoalNode.Location.X);
    }

    [Test]
    public async Task RoadmapGraphProjection_KeepsBlockConnectionsMostlyHorizontal()
    {
        var storage = new StubTaskStorage();
        var target = CreateTask("target", "Add private bool IsEmpty", storage);
        var prerequisite = CreateTask("prerequisite", "Document reusable set checks", storage);
        var bridge = CreateTask("bridge", "Extract common IntSet helper", storage);
        var nearTarget = CreateTask("near-target", "Add small guard", storage);
        var fillerA = CreateTask("filler-a", "SortedSetComparable", storage);
        var fillerB = CreateTask("filler-b", "IsFullSet", storage);
        var fillerC = CreateTask("filler-c", "Document IntSet ordering", storage);
        var blocker = CreateTask("blocker", "123", storage);

        prerequisite.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { bridge },
            Array.Empty<TaskItemViewModel>());
        bridge.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { target },
            Array.Empty<TaskItemViewModel>());
        blocker.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { target, nearTarget },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(
            target,
            fillerA,
            fillerB,
            prerequisite,
            fillerC,
            bridge,
            nearTarget,
            blocker));
        var blockConnection = projection.Connections.Single(connection =>
            connection.Kind == RoadmapConnectionKind.Blocks &&
            connection.Tail.TaskItem == blocker &&
            connection.Head.TaskItem == target);
        var bridgeConnection = projection.Connections.Single(connection =>
            connection.Kind == RoadmapConnectionKind.Blocks &&
            connection.Tail.TaskItem == bridge &&
            connection.Head.TaskItem == target);

        await Assert.That(blockConnection.Head.Location.X).IsGreaterThan(blockConnection.Tail.Location.X);
        await Assert.That(blockConnection.IsLeftToRight).IsTrue();
        await Assert.That(bridgeConnection.IsLeftToRight).IsTrue();
        await Assert.That(Math.Abs(blockConnection.Source.Y - blockConnection.Target.Y))
            .IsLessThan(RoadmapNode.Height * 2);
        await Assert.That(Math.Abs(bridgeConnection.Source.Y - bridgeConnection.Target.Y))
            .IsLessThan(RoadmapNode.Height * 2);
    }

    [Test]
    public async Task RoadmapGraphProjection_HidesDirectConnectionWhenLongerAchievementPathExists()
    {
        var storage = new StubTaskStorage();
        var start = CreateTask("start", "Start", storage);
        var middle = CreateTask("middle", "Middle", storage);
        var goal = CreateTask("goal", "Goal", storage);

        start.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { middle, goal },
            Array.Empty<TaskItemViewModel>());
        middle.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { goal },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(start, middle, goal));

        await Assert.That(projection.Connections.Any(connection =>
            connection.Kind == RoadmapConnectionKind.Blocks &&
            connection.Tail.TaskItem == start &&
            connection.Head.TaskItem == middle)).IsTrue();
        await Assert.That(projection.Connections.Any(connection =>
            connection.Kind == RoadmapConnectionKind.Blocks &&
            connection.Tail.TaskItem == middle &&
            connection.Head.TaskItem == goal)).IsTrue();
        await Assert.That(projection.Connections.Any(connection =>
            connection.Kind == RoadmapConnectionKind.Blocks &&
            connection.Tail.TaskItem == start &&
            connection.Head.TaskItem == goal)).IsFalse();
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

        await Assert.That(childToParent.IsLeftToRight).IsTrue();
        await Assert.That(siblingToParent.IsLeftToRight).IsTrue();
        await Assert.That(childToParent.Target.X - childToParent.Source.X).IsGreaterThan(80);
        await Assert.That(siblingToParent.Target.X - siblingToParent.Source.X).IsGreaterThan(80);
        await Assert.That(CountConnectionCrossings(projection.Connections)).IsLessThanOrEqualTo(1);
    }

    [Test]
    public async Task RoadmapGraphProjection_KeepsBlockChainInGoalBand()
    {
        var storage = new StubTaskStorage();
        var fixture = CreateDenseRoadmapFixture(storage);

        var projection = RoadmapGraphBuilder.Build(fixture.Roots);
        var chainNodes = fixture.Chain
            .Select(task => projection.Nodes.Single(node => node.TaskItem == task))
            .ToArray();
        var chainConnections = projection.Connections
            .Where(connection =>
                connection.Kind == RoadmapConnectionKind.Blocks &&
                fixture.Chain.Contains(connection.Tail.TaskItem) &&
                fixture.Chain.Contains(connection.Head.TaskItem))
            .ToArray();
        var unrelatedContains = projection.Connections
            .Where(connection =>
                connection.Kind == RoadmapConnectionKind.Contains &&
                fixture.Fillers.Contains(connection.Tail.TaskItem) &&
                connection.Head.TaskItem == fixture.Root)
            .ToArray();

        await Assert.That(projection.Connections.All(connection => connection.IsLeftToRight)).IsTrue();
        await Assert.That(chainConnections.Length).IsEqualTo(2);
        await Assert.That(GetVerticalSpan(chainNodes)).IsLessThan(RoadmapNode.Height * 4);
        await Assert.That(CountSegmentCrossings(chainConnections, unrelatedContains)).IsEqualTo(0);
    }

    [Test]
    public async Task RoadmapGraphProjection_AvoidsCrossingLongEdgesThroughIntermediateLayers()
    {
        var storage = new StubTaskStorage();
        var start = CreateTask("start", "Start", storage);
        var mainFirst = CreateTask("main-first", "Main first", storage);
        var mainSecond = CreateTask("main-second", "Main second", storage);
        var mainGoal = CreateTask("main-goal", "Main goal", storage);
        var sideFirst = CreateTask("side-first", "Side first", storage);
        var sideSecond = CreateTask("side-second", "Side second", storage);
        var sideGoal = CreateTask("side-goal", "Side goal", storage);

        start.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { mainFirst, sideGoal },
            Array.Empty<TaskItemViewModel>());
        mainFirst.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { mainSecond },
            Array.Empty<TaskItemViewModel>());
        mainSecond.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { mainGoal },
            Array.Empty<TaskItemViewModel>());
        sideFirst.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { sideSecond },
            Array.Empty<TaskItemViewModel>());
        sideSecond.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { sideGoal },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(
            mainGoal,
            sideGoal,
            mainSecond,
            sideSecond,
            mainFirst,
            sideFirst,
            start));

        await Assert.That(projection.Connections.All(connection => connection.IsLeftToRight)).IsTrue();
        await Assert.That(CountSegmentCrossings(projection.Connections)).IsEqualTo(0);
    }

    [Test]
    public async Task RoadmapGraphProjection_KeepsMixedPrerequisiteChainNearItsGoal()
    {
        var storage = new StubTaskStorage();
        var root = CreateTask("mixed-root", "Root goal", storage);
        var feature = CreateTask("mixed-feature", "Feature goal", storage);
        var goal = CreateTask("mixed-goal", "Mobile release goal", storage);
        var blockedByBuild = CreateTask("mixed-blocked-by-build", "Publish Android package", storage);
        var buildScript = CreateTask("mixed-build-script", "Add Android build script", storage);
        var leftStart = CreateTask("mixed-left-start", "Fix libgit2 Android build", storage);
        var rootFillers = Enumerable.Range(0, 12)
            .Select(index => CreateTask(
                $"mixed-root-filler-{index}",
                $"Root sibling {index}",
                storage))
            .ToArray();
        var featureFillers = Enumerable.Range(0, 10)
            .Select(index => CreateTask(
                $"mixed-feature-filler-{index}",
                $"Feature sibling {index}",
                storage))
            .ToArray();

        leftStart.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { buildScript },
            Array.Empty<TaskItemViewModel>());
        goal.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { blockedByBuild },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappersFromWrappers(
            CreateWrapper(
                root,
                rootFillers
                    .Select(task => CreateWrapper(task))
                    .Concat(new[]
                    {
                        CreateWrapper(
                            feature,
                            featureFillers
                                .Select(task => CreateWrapper(task))
                                .Concat(new[]
                                {
                                    CreateWrapper(goal, CreateWrapper(buildScript)),
                                    CreateWrapper(blockedByBuild)
                                })
                                .ToArray()),
                        CreateWrapper(leftStart)
                    })
                    .ToArray())));
        var chainNodes = new[] { leftStart, buildScript, goal, blockedByBuild }
            .Select(task => projection.Nodes.Single(node => node.TaskItem == task))
            .ToArray();
        var buildScriptToGoal = projection.Connections.Single(connection =>
            connection.Tail.TaskItem == buildScript &&
            connection.Head.TaskItem == goal);
        var goalToBlocked = projection.Connections.Single(connection =>
            connection.Tail.TaskItem == goal &&
            connection.Head.TaskItem == blockedByBuild);

        await Assert.That(projection.Connections.All(connection => connection.IsLeftToRight)).IsTrue();
        await Assert.That(GetVerticalSpan(chainNodes)).IsLessThan(RoadmapNode.Height * 5);
        await Assert.That(Math.Abs(buildScriptToGoal.Source.Y - buildScriptToGoal.Target.Y))
            .IsLessThan(RoadmapNode.Height * 2);
        await Assert.That(Math.Abs(goalToBlocked.Source.Y - goalToBlocked.Target.Y))
            .IsLessThan(RoadmapNode.Height * 2);
    }

    [Test]
    public async Task RoadmapGraphProjection_KeepsLongLeftPathNearDenseTargetBand()
    {
        var storage = new StubTaskStorage();
        var root = CreateTask("dense-root", "Root goal", storage);
        var feature = CreateTask("dense-feature", "Feature branch", storage);
        var target = CreateTask("dense-target", "Dense target", storage);
        var leftStart = CreateTask("dense-left-start", "Left start", storage);
        var leftMiddle = CreateTask("dense-left-middle", "Left middle", storage);
        var leftEnd = CreateTask("dense-left-end", "Left end", storage);
        var rootFillers = Enumerable.Range(0, 24)
            .Select(index => CreateTask(
                $"dense-root-filler-{index}",
                $"Root filler {index}",
                storage))
            .ToArray();
        var featureFillers = Enumerable.Range(0, 24)
            .Select(index => CreateTask(
                $"dense-feature-filler-{index}",
                $"Feature filler {index}",
                storage))
            .ToArray();

        leftStart.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { leftMiddle },
            Array.Empty<TaskItemViewModel>());
        leftMiddle.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { leftEnd },
            Array.Empty<TaskItemViewModel>());
        leftEnd.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { target },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappersFromWrappers(
            CreateWrapper(
                root,
                new[]
                {
                    CreateWrapper(
                        feature,
                        new[] { CreateWrapper(target) }
                            .Concat(featureFillers.Select(task => CreateWrapper(task)))
                            .ToArray())
                }
                    .Concat(rootFillers.Select(task => CreateWrapper(task)))
                    .Concat(new[]
                    {
                        CreateWrapper(leftStart),
                        CreateWrapper(leftMiddle),
                        CreateWrapper(leftEnd)
                    })
                    .ToArray())));
        var chainConnections = projection.Connections
            .Where(connection =>
                connection.Kind == RoadmapConnectionKind.Blocks &&
                (connection.Tail.TaskItem == leftStart ||
                 connection.Tail.TaskItem == leftMiddle ||
                 connection.Tail.TaskItem == leftEnd))
            .ToArray();

        await Assert.That(projection.Connections.All(connection => connection.IsLeftToRight)).IsTrue();
        await Assert.That(chainConnections.Length).IsEqualTo(3);
        await Assert.That(chainConnections.Max(connection =>
                Math.Abs(connection.Source.Y - connection.Target.Y)))
            .IsLessThan(RoadmapNode.Height);
    }

    [Test]
    public async Task RoadmapGraphProjection_PullsConnectedLeftSourceAboveUnrelatedSibling()
    {
        var storage = new StubTaskStorage();
        var unrelated = CreateTask("left-unrelated", "Unrelated left item", storage);
        var source = CreateTask("left-source", "Connected left source", storage);
        var target = CreateTask("left-target", "Goal near the top", storage);

        source.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { target },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(
            unrelated,
            source,
            target));
        var unrelatedNode = projection.Nodes.Single(node => node.TaskItem == unrelated);
        var sourceNode = projection.Nodes.Single(node => node.TaskItem == source);
        var sourceToTarget = projection.Connections.Single(connection =>
            connection.Tail.TaskItem == source &&
            connection.Head.TaskItem == target);

        await Assert.That(sourceNode.Location.Y).IsLessThan(unrelatedNode.Location.Y);
        await Assert.That(Math.Abs(sourceToTarget.Source.Y - sourceToTarget.Target.Y))
            .IsLessThan(RoadmapNode.Height);
    }

    [Test]
    public async Task RoadmapGraph_OpenView_KeepsDenseBlockChainReadable()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var storage = new StubTaskStorage();
            var fixture = CreateDenseRoadmapFixture(storage);
            var graphViewModel = new GraphViewModel
            {
                Tasks = fixture.Roots,
                UnlockedTasks = fixture.Roots
            };
            Window? window = null;

            try
            {
                var view = new GraphControl { DataContext = graphViewModel };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = WaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var expectedNodeCount = 1 + fixture.Chain.Length + fixture.Fillers.Length;
                var nodesReady = WaitFor(() => graphControl!.RoadmapNodes.Count == expectedNodeCount);
                await Assert.That(nodesReady).IsTrue();

                var chainNodes = fixture.Chain
                    .Select(task => graphControl!.RoadmapNodes.Single(node => node.TaskItem == task))
                    .ToArray();
                var chainConnections = graphControl!.RoadmapConnections
                    .Where(connection =>
                        connection.Kind == RoadmapConnectionKind.Blocks &&
                        fixture.Chain.Contains(connection.Tail.TaskItem) &&
                        fixture.Chain.Contains(connection.Head.TaskItem))
                    .ToArray();
                var unrelatedContains = graphControl.RoadmapConnections
                    .Where(connection =>
                        connection.Kind == RoadmapConnectionKind.Contains &&
                        fixture.Fillers.Contains(connection.Tail.TaskItem) &&
                        connection.Head.TaskItem == fixture.Root)
                    .ToArray();

                await Assert.That(GetVerticalSpan(chainNodes)).IsLessThan(RoadmapNode.Height * 4);
                await Assert.That(CountSegmentCrossings(chainConnections, unrelatedContains)).IsEqualTo(0);
            }
            finally
            {
                window?.Close();
            }
        }, CancellationToken.None);
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
    public async Task RoadmapGraph_UpdateGraphPulse_CoalescesQueuedBackgroundRebuilds()
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
                WaitForStableRoadmapUpdates(graphControl!);

                var buildIndicator = WaitForAutomationControl<Border>(view, "RoadmapBuildIndicator");
                var buildProgressBar = WaitForAutomationControl<ProgressBar>(view, "RoadmapBuildProgressBar");
                var buildProgressText = WaitForAutomationControl<TextBlock>(view, "RoadmapBuildProgressText");

                await Assert.That(buildIndicator.IsVisible).IsFalse();
                await Assert.That(buildProgressBar.Maximum).IsEqualTo(100);

                var updateCountBeforePulse = graphControl!.RoadmapGraphUpdateCount;
                var subscriptionRefreshCountBeforePulse = graphControl.RoadmapScopeSubscriptionRefreshCount;
                var backgroundBuildStartCountBeforePulse = graphControl.RoadmapGraphBackgroundBuildStartCount;

                await Assert.That(backgroundBuildStartCountBeforePulse).IsGreaterThan(0);
                await Assert.That(graphControl.RoadmapLastBuildRanOnUiThread).IsFalse();

                for (var index = 0; index < 3; index++)
                {
                    vm.Graph.UpdateGraph = !vm.Graph.UpdateGraph;
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(40);
                    Dispatcher.UIThread.RunJobs();
                }

                var indicatorVisible = WaitFor(() =>
                    buildIndicator.IsVisible &&
                    graphControl.RoadmapGraphBuildInProgress &&
                    buildProgressBar.Value >= 0 &&
                    buildProgressText.Text?.EndsWith("%", StringComparison.Ordinal) == true);
                await Assert.That(indicatorVisible).IsTrue();
                await Assert.That(graphControl.RoadmapGraphUpdateCount).IsEqualTo(updateCountBeforePulse);

                var rebuilt = WaitFor(
                    () => graphControl.RoadmapGraphUpdateCount > updateCountBeforePulse,
                    5000);
                await Assert.That(rebuilt).IsTrue();

                var updateCountAfterPulse = graphControl.RoadmapGraphUpdateCount;
                var extraRebuild = WaitFor(
                    () => graphControl.RoadmapGraphUpdateCount > updateCountAfterPulse,
                    400);

                await Assert.That(updateCountAfterPulse).IsEqualTo(updateCountBeforePulse + 1);
                await Assert.That(graphControl.RoadmapGraphBackgroundBuildStartCount)
                    .IsEqualTo(backgroundBuildStartCountBeforePulse + 1);
                await Assert.That(graphControl.RoadmapLastBuildRanOnUiThread).IsFalse();
                await Assert.That(graphControl.RoadmapScopeSubscriptionRefreshCount)
                    .IsEqualTo(subscriptionRefreshCountBeforePulse);
                await Assert.That(extraRebuild).IsFalse();
                await Assert.That(buildIndicator.IsVisible).IsFalse();
                await Assert.That(graphControl.RoadmapLastBuildTime).IsGreaterThan(TimeSpan.Zero);
                await Assert.That(graphControl.RoadmapLastApplyProjectionTime).IsGreaterThan(TimeSpan.Zero);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_FilterChange_CancelsRunningBackgroundRebuildAndStartsLatest()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            GraphControl? graphControl = null;
            using var firstBuildEntered = new ManualResetEventSlim();
            using var firstBuildCanFinish = new ManualResetEventSlim();
            var buildCalls = 0;

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

                graphControl = WaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var nodesReady = WaitFor(() => graphControl!.RoadmapNodes.Count > 0);
                await Assert.That(nodesReady).IsTrue();
                WaitForStableRoadmapUpdates(graphControl!);

                var filterReady = WaitFor(() =>
                    vm.Graph.EmojiExcludeFilters.Any(filter =>
                        !string.IsNullOrEmpty(filter.Emoji) &&
                        !filter.ShowTasks &&
                        filter.Source != null &&
                        FindRoadmapNode(graphControl!, filter.Source.Id) != null));
                await Assert.That(filterReady).IsTrue();

                var excludeFilter = vm.Graph.EmojiExcludeFilters.First(filter =>
                    !string.IsNullOrEmpty(filter.Emoji) &&
                    !filter.ShowTasks &&
                    filter.Source != null &&
                    FindRoadmapNode(graphControl!, filter.Source.Id) != null);
                var filteredTaskId = excludeFilter.Source!.Id;
                var updateCountBeforeFilter = graphControl!.RoadmapGraphUpdateCount;
                var startCountBeforeFilter = graphControl.RoadmapGraphBackgroundBuildStartCount;
                var cancelRequestCountBeforeFilter = graphControl.RoadmapGraphBackgroundBuildCancelRequestCount;
                var canceledCountBeforeFilter = graphControl.RoadmapGraphBackgroundBuildCanceledCount;

                graphControl.RoadmapGraphBuildOverride = (input, progress, cancellationToken) =>
                {
                    var call = Interlocked.Increment(ref buildCalls);
                    if (call == 1)
                    {
                        firstBuildEntered.Set();
                        while (!firstBuildCanFinish.Wait(10))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }

                    return RoadmapGraphBuilder.Build(input, progress, cancellationToken);
                };

                excludeFilter.ShowTasks = true;
                Dispatcher.UIThread.RunJobs();

                var firstBuildStarted = WaitFor(() => firstBuildEntered.IsSet);
                await Assert.That(firstBuildStarted).IsTrue();
                await Assert.That(Volatile.Read(ref buildCalls)).IsEqualTo(1);

                excludeFilter.ShowTasks = false;
                Dispatcher.UIThread.RunJobs();

                var latestStartedBeforeFirstWasReleased = WaitFor(() =>
                    Volatile.Read(ref buildCalls) >= 2 &&
                    graphControl.RoadmapGraphBackgroundBuildCancelRequestCount > cancelRequestCountBeforeFilter);
                await Assert.That(latestStartedBeforeFirstWasReleased).IsTrue();
                await Assert.That(firstBuildCanFinish.IsSet).IsFalse();

                firstBuildCanFinish.Set();

                var firstBuildCanceled = WaitFor(() =>
                    graphControl.RoadmapGraphBackgroundBuildCanceledCount > canceledCountBeforeFilter);
                await Assert.That(firstBuildCanceled).IsTrue();

                var latestApplied = WaitFor(() =>
                    graphControl.RoadmapGraphUpdateCount > updateCountBeforeFilter &&
                    FindRoadmapNode(graphControl, filteredTaskId) != null,
                    5000);
                await Assert.That(latestApplied).IsTrue();
                await Assert.That(graphControl.RoadmapGraphBackgroundBuildStartCount)
                    .IsGreaterThanOrEqualTo(startCountBeforeFilter + 2);
                await Assert.That(graphControl.RoadmapGraphBuildInProgress).IsFalse();
            }
            finally
            {
                firstBuildCanFinish.Set();
                if (graphControl != null)
                {
                    graphControl.RoadmapGraphBuildOverride = null;
                }

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
                    var outgoingConnection = graphControl!.RoadmapConnections
                        .FirstOrDefault(connection => connection.Tail.Id == MainWindowViewModelFixture.RootTask2Id);
                    return text?.Text == rootTask.TitleWithoutEmoji &&
                           node != null &&
                           outgoingConnection != null &&
                           node.Width < wideWidth - 150 &&
                           node.Width <= RoadmapNode.MinWidth + 1 &&
                           Math.Abs(taskNodeWidth(text) - node.Width) < 0.5 &&
                           Math.Abs(node.RightAnchor.X - (node.Location.X + node.Width)) < 0.5 &&
                           Math.Abs(outgoingConnection.Source.X - node.RightAnchor.X) < 0.5 &&
                           outgoingConnection.HasSourceExtension &&
                           Math.Abs(
                               outgoingConnection.RoutedSource.X -
                               (node.Location.X + RoadmapNode.MaxWidth)) < 0.5;
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

    private static int WaitForStableRoadmapUpdates(
        GraphControl graphControl,
        int quietMilliseconds = 350,
        int timeoutMilliseconds = 3000)
    {
        var lastCount = graphControl.RoadmapGraphUpdateCount;
        var stableSince = DateTime.UtcNow;

        SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var currentCount = graphControl.RoadmapGraphUpdateCount;
            if (currentCount != lastCount)
            {
                lastCount = currentCount;
                stableSince = DateTime.UtcNow;
            }

            return DateTime.UtcNow - stableSince >= TimeSpan.FromMilliseconds(quietMilliseconds) &&
                   !graphControl.RoadmapGraphBuildInProgress;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        return lastCount;
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

    private static DenseRoadmapFixture CreateDenseRoadmapFixture(ITaskStorage storage)
    {
        var root = CreateTask("roadmap-root", "Roadmap root", storage);
        var chainStart = CreateTask("chain-start", "Clarify business rule", storage);
        var chainMiddle = CreateTask("chain-middle", "Prototype rule check", storage);
        var chainGoal = CreateTask("chain-goal", "Ship rule automation", storage);
        var fillers = Enumerable.Range(0, 12)
            .Select(index => CreateTask(
                $"filler-{index}",
                $"Independent roadmap item {index}",
                storage))
            .ToArray();

        chainStart.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { chainMiddle },
            Array.Empty<TaskItemViewModel>());
        chainMiddle.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { chainGoal },
            Array.Empty<TaskItemViewModel>());

        var rootWrapper = CreateWrapper(
            root,
            new[] { CreateWrapper(chainGoal) }
                .Concat(fillers.Select(task => CreateWrapper(task)))
                .Concat(new[]
                {
                    CreateWrapper(chainMiddle),
                    CreateWrapper(chainStart)
                })
                .ToArray());

        return new DenseRoadmapFixture(
            CreateRootWrappersFromWrappers(rootWrapper),
            root,
            new[] { chainStart, chainMiddle, chainGoal },
            fillers);
    }

    private static double GetVerticalSpan(IEnumerable<RoadmapNode> nodes)
    {
        var rows = nodes.Select(node => node.Location.Y).ToArray();
        return rows.Max() - rows.Min();
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

    private static int CountSegmentCrossings(
        IEnumerable<RoadmapConnection> firstConnections,
        IEnumerable<RoadmapConnection> secondConnections)
    {
        var first = firstConnections.ToArray();
        var second = secondConnections.ToArray();
        var crossings = 0;

        foreach (var left in first)
        {
            foreach (var right in second)
            {
                if (SegmentsIntersect(left.Source, left.Target, right.Source, right.Target))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    private static int CountSegmentCrossings(IEnumerable<RoadmapConnection> connections)
    {
        var orderedConnections = connections.ToArray();
        var crossings = 0;

        for (var firstIndex = 0; firstIndex < orderedConnections.Length; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < orderedConnections.Length; secondIndex++)
            {
                var first = orderedConnections[firstIndex];
                var second = orderedConnections[secondIndex];
                if (first.Tail == second.Tail ||
                    first.Tail == second.Head ||
                    first.Head == second.Tail ||
                    first.Head == second.Head)
                {
                    continue;
                }

                if (SegmentsIntersect(first.Source, first.Target, second.Source, second.Target))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    private static bool SegmentsIntersect(
        Avalonia.Point firstStart,
        Avalonia.Point firstEnd,
        Avalonia.Point secondStart,
        Avalonia.Point secondEnd)
    {
        return GetOrientation(firstStart, firstEnd, secondStart) *
               GetOrientation(firstStart, firstEnd, secondEnd) < 0 &&
               GetOrientation(secondStart, secondEnd, firstStart) *
               GetOrientation(secondStart, secondEnd, firstEnd) < 0;
    }

    private static double GetOrientation(
        Avalonia.Point first,
        Avalonia.Point second,
        Avalonia.Point third)
    {
        return (second.X - first.X) * (third.Y - first.Y) -
               (second.Y - first.Y) * (third.X - first.X);
    }

    private sealed record DenseRoadmapFixture(
        ReadOnlyObservableCollection<TaskWrapperViewModel> Roots,
        TaskItemViewModel Root,
        TaskItemViewModel[] Chain,
        TaskItemViewModel[] Fillers);

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
