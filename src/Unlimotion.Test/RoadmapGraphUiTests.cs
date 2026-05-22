using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
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

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
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
    public async Task RoadmapGraphProjection_SharedPrerequisiteUsesLeftmostRequiredColumn()
    {
        var storage = new StubTaskStorage();
        var sharedStart = CreateTask("shared-start", "Shared start", storage);
        var middle = CreateTask("shared-middle", "Middle branch", storage);
        var deepGoal = CreateTask("shared-deep-goal", "Deep goal", storage);
        var shortGoal = CreateTask("shared-short-goal", "Short goal", storage);

        sharedStart.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { shortGoal, middle },
            Array.Empty<TaskItemViewModel>());
        middle.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { deepGoal },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(
            sharedStart,
            shortGoal,
            middle,
            deepGoal));
        var sharedStartNode = projection.Nodes.Single(node => node.TaskItem == sharedStart);
        var middleNode = projection.Nodes.Single(node => node.TaskItem == middle);
        var deepGoalNode = projection.Nodes.Single(node => node.TaskItem == deepGoal);
        var shortGoalNode = projection.Nodes.Single(node => node.TaskItem == shortGoal);

        await Assert.That(projection.Connections.All(connection => connection.IsLeftToRight)).IsTrue();
        await Assert.That(sharedStartNode.Location.X).IsLessThan(middleNode.Location.X);
        await Assert.That(middleNode.Location.X).IsLessThan(deepGoalNode.Location.X);
        await Assert.That(shortGoalNode.Location.X).IsEqualTo(deepGoalNode.Location.X);
    }

    [Test]
    public async Task RoadmapGraphProjection_KeepsAcyclicCycleSubsetLeftToRight()
    {
        var storage = new StubTaskStorage();
        var first = CreateTask("cycle-first", "Cycle first", storage);
        var second = CreateTask("cycle-second", "Cycle second", storage);

        first.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { second },
            Array.Empty<TaskItemViewModel>());
        second.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { first },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(first, second));

        var keptConnection = projection.Connections.Single();

        await Assert.That(projection.Nodes.Any(node => node.TaskItem == first)).IsTrue();
        await Assert.That(projection.Nodes.Any(node => node.TaskItem == second)).IsTrue();
        await Assert.That(keptConnection.Tail.TaskItem).IsEqualTo(first);
        await Assert.That(keptConnection.Head.TaskItem).IsEqualTo(second);
        await Assert.That(keptConnection.IsLeftToRight).IsTrue();
    }

    [Test]
    public async Task RoadmapGraphProjection_RemovesRedundantConnectionsAfterBreakingCycles()
    {
        var storage = new StubTaskStorage();
        var source = CreateTask("cycle-source", "Cycle source", storage);
        var first = CreateTask("cycle-first-target", "Cycle first target", storage);
        var second = CreateTask("cycle-second-target", "Cycle second target", storage);

        source.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { first, second },
            Array.Empty<TaskItemViewModel>());
        first.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { second },
            Array.Empty<TaskItemViewModel>());
        second.ApplyRelations(
            Array.Empty<TaskItemViewModel>(),
            Array.Empty<TaskItemViewModel>(),
            new[] { first },
            Array.Empty<TaskItemViewModel>());

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(source, first, second));

        await Assert.That(projection.Connections.All(connection => connection.IsLeftToRight)).IsTrue();
        await Assert.That(projection.Connections.Any(connection =>
            connection.Tail.TaskItem == source &&
            connection.Head.TaskItem == first)).IsTrue();
        await Assert.That(projection.Connections.Any(connection =>
            connection.Tail.TaskItem == first &&
            connection.Head.TaskItem == second)).IsTrue();
        await Assert.That(projection.Connections.Any(connection =>
            connection.Tail.TaskItem == second &&
            connection.Head.TaskItem == first)).IsFalse();
    }

    [Test]
    public async Task RoadmapGraphProjection_HandlesDeepDependencyChainWithoutRecursiveLayout()
    {
        const int TaskCount = 4096;
        var storage = new StubTaskStorage();
        var tasks = Enumerable.Range(0, TaskCount)
            .Select(index => CreateTask($"deep-chain-{index}", $"Deep chain {index}", storage))
            .ToArray();

        for (var index = 0; index < tasks.Length - 1; index++)
        {
            tasks[index].ApplyRelations(
                Array.Empty<TaskItemViewModel>(),
                Array.Empty<TaskItemViewModel>(),
                new[] { tasks[index + 1] },
                Array.Empty<TaskItemViewModel>());
        }

        var projection = RoadmapGraphBuilder.Build(CreateRootWrappers(tasks));
        var firstNode = projection.Nodes.Single(node => node.TaskItem == tasks[0]);
        var lastNode = projection.Nodes.Single(node => node.TaskItem == tasks[^1]);

        await Assert.That(projection.Nodes.Count).IsEqualTo(TaskCount);
        await Assert.That(projection.Connections.Count).IsEqualTo(TaskCount - 1);
        await Assert.That(projection.Connections.All(connection => connection.IsLeftToRight)).IsTrue();
        await Assert.That(firstNode.Location.X).IsLessThan(lastNode.Location.X);
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
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
    public async Task RoadmapGraph_ViewportOverlays_CollapseToCompactButtonsAndRestore()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                window = CreateWindow(view, 420, 520);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var nodesReady = WaitFor(() => graphControl!.RoadmapNodes.Count > 0);
                await Assert.That(nodesReady).IsTrue();

                var editor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                var toolbar = WaitForAutomationControl<Control>(view, "RoadmapViewportToolbar");
                var minimapPanel = WaitForAutomationControl<Control>(view, "RoadmapMinimapPanel");
                var minimap = WaitForAutomationControl<Control>(view, "RoadmapMinimap");
                var toolbarCollapseButton = WaitForAutomationControl<Button>(view, "RoadmapViewportToolbarCollapseButton");
                var minimapCollapseButton = WaitForAutomationControl<Button>(view, "RoadmapMinimapCollapseButton");

                await Assert.That(IsVisibleAndArranged(toolbar)).IsTrue();
                await Assert.That(IsVisibleAndArranged(minimapPanel)).IsTrue();
                await Assert.That(IsVisibleAndArranged(minimap)).IsTrue();

                await ClickControlAsync(window, minimapCollapseButton);
                var minimapCollapsed = WaitFor(() => !minimapPanel.IsVisible);
                await Assert.That(minimapCollapsed).IsTrue();

                var minimapExpandButton = WaitForAutomationControl<Button>(view, "RoadmapMinimapExpandButton");
                await Assert.That(IsVisibleAndArranged(minimapExpandButton)).IsTrue();
                await Assert.That(minimapExpandButton.Bounds.Width).IsLessThanOrEqualTo(40);
                await Assert.That(minimapExpandButton.Bounds.Height).IsLessThanOrEqualTo(40);
                await Assert.That(IsVisibleAndArranged(toolbar)).IsTrue();

                var locationProperty = editor.GetType().GetProperty("ViewportLocation")!;
                var panRightButton = WaitForAutomationControl<Button>(view, "RoadmapPanRightButton");
                var locationBeforePan = (Avalonia.Point)locationProperty.GetValue(editor)!;

                await ClickControlAsync(window, panRightButton);
                var toolbarClickable = WaitFor(() =>
                    ((Avalonia.Point)locationProperty.GetValue(editor)!).X > locationBeforePan.X);
                await Assert.That(toolbarClickable).IsTrue();

                await ClickControlAsync(window, toolbarCollapseButton);
                var toolbarCollapsed = WaitFor(() => !toolbar.IsVisible);
                await Assert.That(toolbarCollapsed).IsTrue();

                var toolbarExpandButton = WaitForAutomationControl<Button>(view, "RoadmapViewportToolbarExpandButton");
                await Assert.That(IsVisibleAndArranged(toolbarExpandButton)).IsTrue();
                await Assert.That(toolbarExpandButton.Bounds.Width).IsLessThanOrEqualTo(40);
                await Assert.That(toolbarExpandButton.Bounds.Height).IsLessThanOrEqualTo(40);

                await ClickControlAsync(window, toolbarExpandButton);
                var toolbarExpanded = WaitFor(() => IsVisibleAndArranged(toolbar));
                await Assert.That(toolbarExpanded).IsTrue();

                await ClickControlAsync(window, minimapExpandButton);
                var minimapExpanded = WaitFor(() =>
                    IsVisibleAndArranged(minimapPanel) &&
                    IsVisibleAndArranged(minimap));
                await Assert.That(minimapExpanded).IsTrue();

                var zoomProperty = editor.GetType().GetProperty("ViewportZoom")!;
                var initialZoom = (double)zoomProperty.GetValue(editor)!;
                var zoomInButton = WaitForAutomationControl<Button>(view, "RoadmapZoomInButton");
                await ClickControlAsync(window, zoomInButton);
                var zoomed = WaitFor(() => (double)zoomProperty.GetValue(editor)! > initialZoom);
                await Assert.That(zoomed).IsTrue();

                var expectedLocation = new Avalonia.Point(123, 45);
                locationProperty.SetValue(editor, expectedLocation);
                Dispatcher.UIThread.RunJobs();

                var minimapLocationProperty = minimap.GetType().GetProperty("ViewportLocation")!;
                var minimapBound = WaitFor(() =>
                {
                    var actual = (Avalonia.Point)minimapLocationProperty.GetValue(minimap)!;
                    return Math.Abs(actual.X - expectedLocation.X) < 0.001 &&
                           Math.Abs(actual.Y - expectedLocation.Y) < 0.001;
                });
                await Assert.That(minimapBound).IsTrue();
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
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
                }

                var indicatorVisible = WaitFor(() =>
                    buildIndicator.IsVisible &&
                    graphControl.RoadmapGraphBuildInProgress &&
                    buildProgressBar.Value >= 0 &&
                    buildProgressText.Text?.EndsWith("%", StringComparison.Ordinal) == true);
                await Assert.That(indicatorVisible).IsTrue();
                await Assert.That(graphControl.RoadmapGraphUpdateCount).IsEqualTo(updateCountBeforePulse);

                var rebuilt = await WaitForRoadmapUpdateAsync(
                    graphControl,
                    updateCountBeforePulse);
                await Assert.That(rebuilt).IsTrue();

                var updateCountAfterPulse = graphControl.RoadmapGraphUpdateCount;
                var extraRebuild = await WaitForRoadmapUpdateAsync(
                    graphControl,
                    updateCountAfterPulse,
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                AddVisibleRoadmapFilterEmoji(vm);
                vm.AllTasksMode = false;
                vm.GraphMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
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

                var firstBuildStarted = await WaitForAsync(() => firstBuildEntered.IsSet);
                await Assert.That(firstBuildStarted).IsTrue();
                await Assert.That(Volatile.Read(ref buildCalls)).IsEqualTo(1);

                excludeFilter.ShowTasks = false;
                Dispatcher.UIThread.RunJobs();

                var latestStartedBeforeFirstWasReleased = await WaitForAsync(() =>
                    Volatile.Read(ref buildCalls) >= 2 &&
                    graphControl.RoadmapGraphBackgroundBuildCancelRequestCount > cancelRequestCountBeforeFilter);
                await Assert.That(latestStartedBeforeFirstWasReleased).IsTrue();
                await Assert.That(firstBuildCanFinish.IsSet).IsFalse();

                firstBuildCanFinish.Set();

                var firstBuildCanceled = await WaitForAsync(() =>
                    graphControl.RoadmapGraphBackgroundBuildCanceledCount > canceledCountBeforeFilter);
                await Assert.That(firstBuildCanceled).IsTrue();

                var latestApplied = await WaitForAsync(() =>
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
    public async Task RoadmapGraph_TitleRename_UpdatesTextWithoutRebuildingMap()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var originalNode = FindRoadmapNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(originalNode).IsNotNull();

                var rootTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(rootTask).IsNotNull();

                WaitForStableRoadmapUpdates(graphControl!);
                var updateCountBeforeRename = graphControl.RoadmapGraphUpdateCount;
                var locationBeforeRename = originalNode!.Location;
                var connectionCountBeforeRename = graphControl.RoadmapConnections.Count;

                rootTask!.Title = "Renamed roadmap task without rebuild";
                var textUpdated = WaitFor(() =>
                {
                    var text = FindTaskTitleTextBlock(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                    var node = FindRoadmapNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                    return text?.Text == rootTask.TitleWithoutEmoji &&
                           ReferenceEquals(originalNode, node);
                });
                await Assert.That(textUpdated).IsTrue();

                var rebuildHappened = WaitFor(
                    () => graphControl.RoadmapGraphUpdateCount > updateCountBeforeRename,
                    1200);

                await Assert.That(rebuildHappened).IsFalse();
                await Assert.That(graphControl.RoadmapGraphUpdateCount).IsEqualTo(updateCountBeforeRename);
                await Assert.That(graphControl.RoadmapConnections.Count).IsEqualTo(connectionCountBeforeRename);
                await Assert.That(originalNode.Location).IsEqualTo(locationBeforeRename);
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
    public async Task RoadmapGraph_InlineTitleEdit_CreatesEditorForF2OrRepeatedTitleClick()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var currentTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(currentTask).IsNotNull();
                currentTask!.Title =
                    "Roadmap editable title with enough words to wrap across multiple visual lines in a roadmap node";

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                var fitButton = WaitForAutomationControl<Button>(view, "RoadmapFitButton");
                await ClickControlAsync(window, fitButton);
                Dispatcher.UIThread.RunJobs();

                var titleText = WaitForTaskTitleTextBlock(
                    graphControl!,
                    MainWindowViewModelFixture.RootTask2Id);
                var titleSurface = WaitForRoadmapInlineTitleSurface(
                    graphControl!,
                    MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(titleText.Bounds.Width).IsGreaterThan(0);
                await Assert.That(titleText.Bounds.Height).IsGreaterThan(22);
                await Assert.That(titleSurface.Bounds.Width).IsGreaterThan(0);
                await Assert.That(FindRoadmapInlineTitleEditor(
                    graphControl!,
                    MainWindowViewModelFixture.RootTask2Id)).IsNull();

                PressRoadmapTitleSurface(titleSurface);
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(FindRoadmapInlineTitleEditor(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id)).IsNull();

                PressRoadmapTitleSurface(titleSurface);
                await Assert.That(FindRoadmapInlineTitleEditor(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id)).IsNull();

                await Task.Delay(650);
                PressRoadmapTitleSurface(titleSurface);
                await Assert.That(FindRoadmapInlineTitleEditor(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id)).IsNull();

                await Task.Delay(650);
                PressRoadmapTitleSurface(titleSurface);

                var inlineEditor = WaitForRoadmapInlineTitleEditor(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id);
                var clickFocused = WaitFor(() => IsFocused(window, inlineEditor));
                await Assert.That(clickFocused).IsTrue();
                await AssertRoadmapInlineTitleEditorHasNoFrame(inlineEditor);
                await Assert.That(inlineEditor.TextWrapping).IsEqualTo(TextWrapping.Wrap);
                await Assert.That(WaitFor(() =>
                    inlineEditor.Bounds.Height >= titleText.Bounds.Height - 1)).IsTrue();

                inlineEditor.Text = "Roadmap renamed from repeated title click";
                Dispatcher.UIThread.RunJobs();
                await Assert.That(currentTask.Title).IsEqualTo("Roadmap renamed from repeated title click");

                var roadmapEditor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                roadmapEditor.Focus();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(WaitFor(() => FindRoadmapInlineTitleEditor(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id) == null)).IsTrue();

                PressHotkey(window, Key.F2, PhysicalKey.F2, RawInputModifiers.None);

                inlineEditor = WaitForRoadmapInlineTitleEditor(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id);
                var hotkeyFocused = WaitFor(() => IsFocused(window, inlineEditor));
                await Assert.That(hotkeyFocused).IsTrue();
                await AssertRoadmapInlineTitleEditorHasNoFrame(inlineEditor);
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                await Assert.That(graphControl!.DataContext).IsSameReferenceAs(vm.Graph);

                var newTask = await vm.taskRepository!.Add();
                newTask.Title = "Roadmap live child";
                var projectionReady = WaitFor(() =>
                    vm.Graph.Tasks.Any(wrapper => wrapper.TaskItem.Id == newTask.Id));
                await Assert.That(projectionReady).IsTrue();
                await Assert.That(RoadmapGraphBuilder.Build(vm.Graph.Tasks)
                    .Nodes.Any(node => node.Id == newTask.Id)).IsTrue();

                var updateCountBeforeAdd = graphControl!.RoadmapGraphUpdateCount;
                vm.Graph.UpdateGraph = !vm.Graph.UpdateGraph;
                var rebuilt = await WaitForRoadmapUpdateAsync(graphControl, updateCountBeforeAdd);
                await Assert.That(rebuilt).IsTrue();

                var appeared = WaitFor(() =>
                    graphControl!.RoadmapNodes.Any(node => node.Id == newTask.Id),
                    5000);
                await Assert.That(appeared).IsTrue();

                await vm.taskRepository.Delete(newTask);
                var deletedFromProjection = WaitFor(() =>
                    vm.Graph.Tasks.All(wrapper => wrapper.TaskItem.Id != newTask.Id));
                await Assert.That(deletedFromProjection).IsTrue();

                var updateCountBeforeDelete = graphControl!.RoadmapGraphUpdateCount;
                vm.Graph.UpdateGraph = !vm.Graph.UpdateGraph;
                var rebuiltAfterDelete = await WaitForRoadmapUpdateAsync(
                    graphControl,
                    updateCountBeforeDelete);
                await Assert.That(rebuiltAfterDelete).IsTrue();

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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                AddVisibleRoadmapFilterEmoji(vm);
                vm.AllTasksMode = false;
                vm.GraphMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
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
                var excludedNodeControl = WaitForTaskNode(graphControl!, excludedTaskId);

                ApplyRoadmapClickSelectionForTest(
                    graphControl!,
                    excludedNodeControl,
                    excludeFilter.Source,
                    KeyModifiers.None);
                await Assert.That(FindRoadmapNode(graphControl!, excludedTaskId)?.IsSelected).IsTrue();

                var updateCountBeforeFilterOut = graphControl!.RoadmapGraphUpdateCount;
                excludeFilter.ShowTasks = true;
                var rebuiltAfterFilterOut = await WaitForRoadmapUpdateAsync(
                    graphControl,
                    updateCountBeforeFilterOut);
                await Assert.That(rebuiltAfterFilterOut).IsTrue();

                var filteredOut = WaitFor(() =>
                    FindRoadmapNode(graphControl!, excludedTaskId) == null &&
                    FindTaskTitleTextBlock(graphControl!, excludedTaskId) == null);
                await Assert.That(filteredOut).IsTrue();
                await Assert.That(GetSelectedRoadmapTaskIds(graphControl!).Contains(excludedTaskId)).IsFalse();

                var updateCountBeforeRestore = graphControl.RoadmapGraphUpdateCount;
                excludeFilter.ShowTasks = false;
                var rebuiltAfterRestore = await WaitForRoadmapUpdateAsync(
                    graphControl,
                    updateCountBeforeRestore);
                await Assert.That(rebuiltAfterRestore).IsTrue();

                var restored = WaitFor(() =>
                    FindRoadmapNode(graphControl!, excludedTaskId) != null &&
                    FindTaskTitleTextBlock(graphControl!, excludedTaskId) != null);
                await Assert.That(restored).IsTrue();
                await Assert.That(FindRoadmapNode(graphControl!, excludedTaskId)?.IsSelected).IsFalse();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_NodeSelectionResolvesOwnerWithoutStaticSingleton()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var previousMainWindow = TaskItemViewModel.MainWindowInstance;
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;
                vm.DetailsAreOpen = false;
                vm.CurrentTaskItem = null;
                TaskItemViewModel.MainWindowInstance = null;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                await Assert.That((graphControl!.DataContext as GraphViewModel)?.MainWindowViewModel)
                    .IsSameReferenceAs(vm);

                var taskNode = WaitForTaskNode(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id);

                var selectRoadmapTask = typeof(GraphControl).GetMethod(
                    "SelectRoadmapTask",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                await Assert.That(selectRoadmapTask).IsNotNull();

                var node = (RoadmapNode)taskNode.DataContext!;
                var owner = selectRoadmapTask!.Invoke(
                    graphControl,
                    new object?[] { taskNode, node.TaskItem });

                await Assert.That(owner).IsSameReferenceAs(vm);
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask2Id);
            }
            finally
            {
                TaskItemViewModel.MainWindowInstance = previousMainWindow;
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_NodeClickSelection_AppliesModifierSemanticsAndVisualState()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                vm.CurrentTaskItem = null;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var root2 = WaitForTaskNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                var root3 = WaitForTaskNode(graphControl, MainWindowViewModelFixture.RootTask3Id);
                var blocked = WaitForTaskNode(graphControl, MainWindowViewModelFixture.BlockedTask2Id);
                var root2Node = (RoadmapNode)root2.DataContext!;
                var root3Node = (RoadmapNode)root3.DataContext!;
                var blockedNode = (RoadmapNode)blocked.DataContext!;

                await ClickControlAsync(window, root2);
                await Assert.That(root2Node.IsSelected).IsTrue();
                await Assert.That(root2Node.IsCurrent).IsTrue();
                await Assert.That(root2.Classes.Contains("roadmapSelected")).IsTrue();
                await Assert.That(root2.Classes.Contains("roadmapCurrent")).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(new[]
                {
                    MainWindowViewModelFixture.RootTask2Id
                });

                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Control);
                await Assert.That(root2Node.IsSelected).IsTrue();
                await Assert.That(root3Node.IsSelected).IsTrue();
                await Assert.That(root3Node.IsCurrent).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask3Id);

                await ClickControlAsync(window, root2, modifiers: RawInputModifiers.Control);
                await Assert.That(root2Node.IsSelected).IsFalse();
                await Assert.That(root3Node.IsSelected).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask3Id);

                await ClickControlAsync(window, blocked, modifiers: RawInputModifiers.Shift);
                await Assert.That(root3Node.IsSelected).IsTrue();
                await Assert.That(blockedNode.IsSelected).IsTrue();
                await Assert.That(blockedNode.IsCurrent).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.BlockedTask2Id);

                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Alt);
                await Assert.That(root3Node.IsSelected).IsFalse();
                await Assert.That(blockedNode.IsSelected).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.BlockedTask2Id);

                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Control);
                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Control);

                await Assert.That(root3Node.IsSelected).IsTrue();
                await Assert.That(root3Node.IsCurrent).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_ModifierDoubleClick_PreservesCurrentTaskSemantics()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                vm.CurrentTaskItem = null;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var root2 = WaitForTaskNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                var root3 = WaitForTaskNode(graphControl, MainWindowViewModelFixture.RootTask3Id);
                var blocked = WaitForTaskNode(graphControl, MainWindowViewModelFixture.BlockedTask2Id);
                var root2Node = (RoadmapNode)root2.DataContext!;

                await ClickControlAsync(window, blocked);
                await ClickControlAsync(window, root2, modifiers: RawInputModifiers.Control);
                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Shift);
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask3Id);

                await ClickControlAsync(window, root2, modifiers: RawInputModifiers.Control);
                await ClickControlAsync(window, root2, modifiers: RawInputModifiers.Control);

                await Assert.That(root2Node.IsSelected).IsFalse();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask3Id);

                await Task.Delay(600);
                await ClickControlAsync(window, root2, modifiers: RawInputModifiers.Shift);
                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Shift);
                await Assert.That(root2Node.IsSelected).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask3Id);

                await Task.Delay(600);
                await ClickControlAsync(window, root2, modifiers: RawInputModifiers.Alt);
                await ClickControlAsync(window, root2, modifiers: RawInputModifiers.Alt);

                await Assert.That(root2Node.IsSelected).IsFalse();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask3Id);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_ModifierDoubleClickSuppression_UsesClickCountWithoutLocalTimeWindow()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var blocked = WaitForTaskNode(graphControl!, MainWindowViewModelFixture.BlockedTask2Id);
                var root2 = WaitForTaskNode(graphControl, MainWindowViewModelFixture.RootTask2Id);
                var root3 = WaitForTaskNode(graphControl, MainWindowViewModelFixture.RootTask3Id);
                var root2Node = (RoadmapNode)root2.DataContext!;

                await ClickControlAsync(window, blocked);
                await ClickControlAsync(window, root2, modifiers: RawInputModifiers.Control);
                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Shift);
                await Assert.That(root2Node.IsSelected).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask3Id);

                PressRoadmapNode(root2, KeyModifiers.Control, clickCount: 1);
                await Assert.That(root2Node.IsSelected).IsFalse();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask3Id);

                await Task.Delay(600);
                PressRoadmapNode(root2, KeyModifiers.Control, clickCount: 2);

                await Assert.That(root2Node.IsSelected).IsFalse();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask3Id);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_SelectedNodeFrame_DoesNotResizeNodeOrShiftContent()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                vm.CurrentTaskItem = null;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var taskNode = WaitForTaskNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                var titleSurface = WaitForRoadmapInlineTitleSurface(
                    graphControl,
                    MainWindowViewModelFixture.RootTask2Id);
                var nodeSizeBefore = taskNode.Bounds.Size;
                var titleBoundsBefore = GetControlBounds(taskNode, titleSurface);

                await ClickControlAsync(window, taskNode);

                var titleBoundsAfter = GetControlBounds(taskNode, titleSurface);
                await Assert.That(Math.Abs(taskNode.Bounds.Width - nodeSizeBefore.Width)).IsLessThan(0.001);
                await Assert.That(Math.Abs(taskNode.Bounds.Height - nodeSizeBefore.Height)).IsLessThan(0.001);
                await Assert.That(Math.Abs(titleBoundsAfter.X - titleBoundsBefore.X)).IsLessThan(0.001);
                await Assert.That(Math.Abs(titleBoundsAfter.Y - titleBoundsBefore.Y)).IsLessThan(0.001);
                await Assert.That(Math.Abs(titleBoundsAfter.Width - titleBoundsBefore.Width)).IsLessThan(0.001);
                await Assert.That(Math.Abs(titleBoundsAfter.Height - titleBoundsBefore.Height)).IsLessThan(0.001);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_RectangleSelection_AppliesModifierSemanticsAndDoesNotChangeCurrentTask()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                var editor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                var selectionRectangle = WaitForAutomationControl<Border>(view, "RoadmapSelectionRectangle");
                var root2 = WaitForTaskNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);

                await ClickControlAsync(window, root2);
                var currentTaskId = vm.CurrentTaskItem?.Id;
                await Assert.That(currentTaskId).IsEqualTo(MainWindowViewModelFixture.RootTask2Id);

                var allNodeBorders = FindRoadmapNodeBorders(graphControl).ToArray();
                var start = FindEmptyEditorPoint(window, editor, allNodeBorders);
                var end = FindRectangleEndPoint(window, editor, start, allNodeBorders);
                var rectangle = CreateNormalizedRect(start, end);
                var hitIds = GetRoadmapNodeIdsIntersectingWindowRect(window, graphControl, rectangle);

                await Assert.That(hitIds.Count).IsGreaterThan(0);

                window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(selectionRectangle.IsVisible).IsTrue();

                window.MouseUp(end, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(selectionRectangle.IsVisible).IsFalse();
                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(hitIds);
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(currentTaskId);

                await DragRoadmapRectangleAsync(window, start, end, RawInputModifiers.Control);
                await Assert.That(GetSelectedRoadmapTaskIds(graphControl).Intersect(hitIds)).IsEmpty();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(currentTaskId);

                await DragRoadmapRectangleAsync(window, start, end, RawInputModifiers.Shift);
                await Assert.That(hitIds.All(id => FindRoadmapNode(graphControl, id)?.IsSelected == true)).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(currentTaskId);

                await DragRoadmapRectangleAsync(window, start, end, RawInputModifiers.Alt);
                await Assert.That(GetSelectedRoadmapTaskIds(graphControl).Intersect(hitIds)).IsEmpty();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(currentTaskId);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_RectangleHitTesting_IgnoresMinimapItems()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                graphControl!.IsRoadmapMinimapExpanded = true;
                Dispatcher.UIThread.RunJobs();

                var editor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                var roadmapSurface = editor.Parent as Visual;
                await Assert.That(roadmapSurface).IsNotNull();
                var minimap = WaitForAutomationControl<Control>(view, "RoadmapMinimap");

                Border? minimapNodeBorder = null;
                var minimapItemReady = WaitFor(() =>
                {
                    minimapNodeBorder = minimap.GetVisualDescendants()
                        .OfType<Border>()
                        .FirstOrDefault(border =>
                            border.DataContext is RoadmapNode &&
                            border.Bounds.Width > 0 &&
                            border.Bounds.Height > 0);
                    return minimapNodeBorder != null;
                });
                await Assert.That(minimapItemReady).IsTrue();

                var minimapNodeBounds = GetControlBounds(roadmapSurface!, minimapNodeBorder!);
                var hits = GetRoadmapNodesIntersectingForTest(graphControl, minimapNodeBounds);

                await Assert.That(hits).IsEmpty();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_RectangleHitTesting_UsesZoomedNodeBounds()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var editor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                var roadmapSurface = editor.Parent as Visual;
                await Assert.That(roadmapSurface).IsNotNull();

                var zoomProperty = editor.GetType().GetProperty("ViewportZoom")!;
                zoomProperty.SetValue(editor, 0.5d);
                var zoomed = WaitFor(() => Math.Abs((double)zoomProperty.GetValue(editor)! - 0.5d) < 0.001);
                await Assert.That(zoomed).IsTrue();

                var hitTestNodeBorder = editor.GetVisualDescendants()
                    .OfType<Border>()
                    .First(border =>
                        border.DataContext is RoadmapNode node &&
                        node.Id == MainWindowViewModelFixture.RootTask2Id &&
                        border.Bounds.Width > 0 &&
                        border.Bounds.Height > 0);
                var unscaledBounds = GetControlBounds(roadmapSurface!, hitTestNodeBorder);
                var zoomedBounds = GetTransformedControlBounds(roadmapSurface, hitTestNodeBorder);
                await Assert.That(unscaledBounds.Width).IsGreaterThan(zoomedBounds.Width + 1);

                var probeRectangle = new Rect(
                    zoomedBounds.Right + (unscaledBounds.Right - zoomedBounds.Right) / 2,
                    zoomedBounds.Y + zoomedBounds.Height / 2,
                    2,
                    2);

                await Assert.That(RectanglesIntersect(probeRectangle, zoomedBounds)).IsFalse();
                await Assert.That(RectanglesIntersect(probeRectangle, unscaledBounds)).IsTrue();

                var hits = GetRoadmapNodesIntersectingForTest(graphControl, probeRectangle);

                await Assert.That(hits.Select(node => node.Id))
                    .DoesNotContain(MainWindowViewModelFixture.RootTask2Id);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_RectangleSelection_UsesViewportZoom()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                graphControl!.IsRoadmapViewportToolbarExpanded = false;
                graphControl.IsRoadmapMinimapExpanded = false;
                Dispatcher.UIThread.RunJobs();

                var editor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                var zoomProperty = editor.GetType().GetProperty("ViewportZoom")!;
                zoomProperty.SetValue(editor, 0.5d);
                var zoomed = WaitFor(() => Math.Abs((double)zoomProperty.GetValue(editor)! - 0.5d) < 0.001);
                await Assert.That(zoomed).IsTrue();

                var taskNode = WaitForTaskNode(graphControl, MainWindowViewModelFixture.RootTask2Id);
                var nodeBounds = GetTransformedControlBounds(window, taskNode);
                var editorBounds = GetControlBounds(window, editor);
                var start = new Point(
                    Math.Max(editorBounds.X + 4, nodeBounds.X - 8),
                    Math.Max(editorBounds.Y + 4, nodeBounds.Y - 8));
                var end = new Point(
                    Math.Min(editorBounds.Right - 4, nodeBounds.Right + 8),
                    Math.Min(editorBounds.Bottom - 4, nodeBounds.Bottom + 8));
                var rectangle = CreateNormalizedRect(start, end);

                await Assert.That(FindRoadmapNodeBorders(graphControl)
                    .Any(border => GetTransformedControlBounds(window, border).Contains(start))).IsFalse();
                var expectedHitIds = GetRoadmapNodeIdsIntersectingTransformedWindowRect(
                    window,
                    graphControl,
                    rectangle);
                await Assert.That(expectedHitIds).Contains(MainWindowViewModelFixture.RootTask2Id);

                await DragRoadmapRectangleAsync(window, start, end, RawInputModifiers.None);

                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(expectedHitIds);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_NodePointerDrag_StartsAfterMoveThresholdAndKeepsSelection()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            Control? taskNode = null;
            var mouseIsDown = false;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;
                vm.DetailsAreOpen = false;
                vm.CurrentTaskItem = null;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                taskNode = WaitForTaskNode(
                    graphControl!,
                    MainWindowViewModelFixture.RootTask2Id);

                var startPoint = GetControlCenterPoint(window, taskNode);
                var movePoint = new Point(startPoint.X + 24, startPoint.Y + 16);
                var dragStartCountBefore = graphControl!.RoadmapDragStartCount;

                window.MouseDown(startPoint, MouseButton.Left, RawInputModifiers.None);
                mouseIsDown = true;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(vm.DetailsAreOpen).IsFalse();

                window.MouseMove(movePoint, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(WaitFor(() =>
                    graphControl.RoadmapDragStartCount == dragStartCountBefore + 1)).IsTrue();

                window.MouseUp(movePoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                mouseIsDown = false;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(vm.DetailsAreOpen).IsFalse();
            }
            finally
            {
                if (mouseIsDown && window is { } topLevel && taskNode != null)
                {
                    topLevel.MouseUp(
                        GetControlCenterPoint(topLevel, taskNode),
                        MouseButton.Left,
                        RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_SelectedNodePlainClickWithoutDrag_CollapsesSelectionOnRelease()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            var mouseIsDown = false;

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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                var root2 = WaitForTaskNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                var root3 = WaitForTaskNode(graphControl, MainWindowViewModelFixture.RootTask3Id);

                await ClickControlAsync(window, root2);
                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Control);
                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(new[]
                {
                    MainWindowViewModelFixture.RootTask2Id,
                    MainWindowViewModelFixture.RootTask3Id
                });

                var point = GetControlCenterPoint(window, root2);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                mouseIsDown = true;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(new[]
                {
                    MainWindowViewModelFixture.RootTask2Id,
                    MainWindowViewModelFixture.RootTask3Id
                });

                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                mouseIsDown = false;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(new[]
                {
                    MainWindowViewModelFixture.RootTask2Id
                });
            }
            finally
            {
                if (mouseIsDown && window != null)
                {
                    window.MouseUp(new Point(0, 0), MouseButton.Left, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_SelectedNodeDrag_PreservesMultiSelectionAfterThreshold()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            Control? taskNode = null;
            var mouseIsDown = false;

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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                taskNode = WaitForTaskNode(graphControl!, MainWindowViewModelFixture.RootTask2Id);
                var root3 = WaitForTaskNode(graphControl, MainWindowViewModelFixture.RootTask3Id);

                await ClickControlAsync(window, taskNode);
                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Control);
                var dragStartCountBefore = graphControl.RoadmapDragStartCount;

                var startPoint = GetControlCenterPoint(window, taskNode);
                var movePoint = new Point(startPoint.X + 24, startPoint.Y + 16);
                window.MouseDown(startPoint, MouseButton.Left, RawInputModifiers.None);
                mouseIsDown = true;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(new[]
                {
                    MainWindowViewModelFixture.RootTask2Id,
                    MainWindowViewModelFixture.RootTask3Id
                });

                window.MouseMove(movePoint, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(WaitFor(() =>
                    graphControl.RoadmapDragStartCount == dragStartCountBefore + 1)).IsTrue();

                window.MouseUp(movePoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                mouseIsDown = false;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(new[]
                {
                    MainWindowViewModelFixture.RootTask2Id,
                    MainWindowViewModelFixture.RootTask3Id
                });
            }
            finally
            {
                if (mouseIsDown && window is { } topLevel && taskNode != null)
                {
                    topLevel.MouseUp(
                        GetControlCenterPoint(topLevel, taskNode),
                        MouseButton.Left,
                        RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_NodeRightDrag_PansViewportWithoutSelectingTask()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            Control? taskNode = null;
            var mouseIsDown = false;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = false;
                vm.GraphMode = true;
                vm.DetailsAreOpen = false;
                vm.CurrentTaskItem = null;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                taskNode = WaitForTaskNode(
                    graphControl!,
                    MainWindowViewModelFixture.RootTask2Id);

                var editor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                var locationProperty = editor.GetType().GetProperty("ViewportLocation")!;
                var startLocation = (Point)locationProperty.GetValue(editor)!;
                var startPoint = GetControlCenterPoint(window, taskNode);
                var movePoint = new Point(startPoint.X + 80, startPoint.Y + 36);

                window.MouseDown(startPoint, MouseButton.Right, RawInputModifiers.None);
                mouseIsDown = true;
                Dispatcher.UIThread.RunJobs();

                window.MouseMove(movePoint, RawInputModifiers.RightMouseButton);
                Dispatcher.UIThread.RunJobs();

                var panned = WaitFor(() =>
                {
                    var currentLocation = (Point)locationProperty.GetValue(editor)!;
                    return Math.Abs(currentLocation.X - startLocation.X) > 1 ||
                           Math.Abs(currentLocation.Y - startLocation.Y) > 1;
                });
                await Assert.That(panned).IsTrue();

                window.MouseUp(movePoint, MouseButton.Right, RawInputModifiers.RightMouseButton);
                mouseIsDown = false;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.CurrentTaskItem).IsNull();
                await Assert.That(vm.DetailsAreOpen).IsFalse();
            }
            finally
            {
                if (mouseIsDown && window is { } topLevel && taskNode != null)
                {
                    topLevel.MouseUp(
                        GetControlCenterPoint(topLevel, taskNode),
                        MouseButton.Right,
                        RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_RightDragOnEmptyCanvas_PansViewportWithSelection()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            var mouseIsDown = false;
            var releasePoint = default(Point);

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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                graphControl!.IsRoadmapViewportToolbarExpanded = false;
                graphControl.IsRoadmapMinimapExpanded = false;
                Dispatcher.UIThread.RunJobs();

                var root2 = WaitForTaskNode(graphControl, MainWindowViewModelFixture.RootTask2Id);
                var root3 = WaitForTaskNode(graphControl, MainWindowViewModelFixture.RootTask3Id);
                await ClickControlAsync(window, root2);
                await ClickControlAsync(window, root3, modifiers: RawInputModifiers.Control);

                var editor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                var locationProperty = editor.GetType().GetProperty("ViewportLocation")!;
                var startLocation = (Point)locationProperty.GetValue(editor)!;
                var startPoint = FindEmptyEditorPoint(window, editor, FindRoadmapNodeBorders(graphControl));
                releasePoint = new Point(startPoint.X + 90, startPoint.Y + 44);

                window.MouseDown(startPoint, MouseButton.Right, RawInputModifiers.None);
                mouseIsDown = true;
                Dispatcher.UIThread.RunJobs();

                window.MouseMove(releasePoint, RawInputModifiers.RightMouseButton);
                Dispatcher.UIThread.RunJobs();

                var panned = WaitFor(() =>
                {
                    var currentLocation = (Point)locationProperty.GetValue(editor)!;
                    return Math.Abs(currentLocation.X - startLocation.X) > 1 ||
                           Math.Abs(currentLocation.Y - startLocation.Y) > 1;
                });
                await Assert.That(panned).IsTrue();

                window.MouseUp(releasePoint, MouseButton.Right, RawInputModifiers.RightMouseButton);
                mouseIsDown = false;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(new[]
                {
                    MainWindowViewModelFixture.RootTask2Id,
                    MainWindowViewModelFixture.RootTask3Id
                });
            }
            finally
            {
                if (mouseIsDown && window is { } topLevel)
                {
                    topLevel.MouseUp(releasePoint, MouseButton.Right, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("CtrlEnter", MainWindowViewModelFixture.RootTask2Id)]
    [Arguments("ShiftEnter", MainWindowViewModelFixture.RootTask2Id)]
    [Arguments("CtrlTab", MainWindowViewModelFixture.RootTask2Id)]
    public async Task RoadmapGraph_CreateHotkeys_UseSelectedRoadmapTaskExactlyOnce(
        string hotkey,
        string selectedTaskId)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var selectedTask = TestHelpers.GetTask(vm, selectedTaskId);
                await Assert.That(selectedTask).IsNotNull();
                var selectedNode = WaitForTaskNode(graphControl!, selectedTaskId);
                selectedNode.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent));
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(selectedTaskId);

                var taskCountBefore = vm.taskRepository!.Tasks.Count;
                var roadmapEditor = WaitForAutomationControl<Control>(view, "RoadmapZoomBorder");
                roadmapEditor.Focus();
                PressRoadmapHotkey(window, hotkey);
                Dispatcher.UIThread.RunJobs();

                var created = WaitFor(() =>
                    vm.taskRepository.Tasks.Count == taskCountBefore + 1 &&
                    vm.CurrentTaskItem != null &&
                    vm.CurrentTaskItem.Id != selectedTaskId);
                await Assert.That(created).IsTrue();

                var createdTask = vm.CurrentTaskItem!;
                await Assert.That(vm.taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + 1);

                switch (hotkey)
                {
                    case "ShiftEnter":
                        await Assert.That(selectedTask!.Blocks).Contains(createdTask.Id);
                        await Assert.That(createdTask.BlockedBy).Contains(selectedTask.Id);
                        break;
                    case "CtrlTab":
                        await Assert.That(selectedTask!.Contains).Contains(createdTask.Id);
                        await Assert.That(createdTask.Parents).Contains(selectedTask.Id);
                        break;
                }
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_TaskCardButtons_AfterRoadmapSelection_CreateExpectedTasks()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                vm.DetailsAreOpen = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();
                var selectedTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(selectedTask).IsNotNull();

                var selectedNode = WaitForTaskNode(graphControl!, selectedTask!.Id);
                await ClickControlAsync(window, selectedNode);
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(selectedTask.Id);
                await Assert.That(vm.DetailsAreOpen).IsTrue();

                var countBefore = vm.taskRepository!.Tasks.Count;
                var createRootButton = FindButtonForCommand(view, vm.Create);
                await ClickControlAsync(window, createRootButton);
                await Assert.That(WaitFor(() => vm.taskRepository.Tasks.Count == countBefore + 1)).IsTrue();
                var rootCreated = vm.CurrentTaskItem;
                await Assert.That(rootCreated).IsNotNull();
                await Assert.That(rootCreated!.Parents).IsEmpty();

                await ClickControlAsync(window, selectedNode);
                await Assert.That(vm.DetailsAreOpen).IsTrue();
                countBefore = vm.taskRepository.Tasks.Count;
                var createSiblingButton = FindButtonForCommand(view, vm.CreateSibling);
                await ClickControlAsync(window, createSiblingButton);
                await Assert.That(WaitFor(() => vm.taskRepository.Tasks.Count == countBefore + 1)).IsTrue();

                await ClickControlAsync(window, selectedNode);
                await Assert.That(vm.DetailsAreOpen).IsTrue();
                countBefore = vm.taskRepository.Tasks.Count;
                var createBlockedSiblingButton = FindButtonForCommand(view, vm.CreateBlockedSibling);
                await ClickControlAsync(window, createBlockedSiblingButton);
                await Assert.That(WaitFor(() => vm.taskRepository.Tasks.Count == countBefore + 1)).IsTrue();
                await Assert.That(selectedTask.Blocks).Contains(vm.CurrentTaskItem!.Id);

                await ClickControlAsync(window, selectedNode);
                await Assert.That(vm.DetailsAreOpen).IsTrue();
                countBefore = vm.taskRepository.Tasks.Count;
                var createInnerButton = FindButtonForCommand(view, vm.CreateInner);
                await ClickControlAsync(window, createInnerButton);
                await Assert.That(WaitFor(() => vm.taskRepository.Tasks.Count == countBefore + 1)).IsTrue();
                await Assert.That(selectedTask.Contains).Contains(vm.CurrentTaskItem!.Id);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_DropWithControl_CreatesBlockingRelationBetweenRoadmapNodes()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var sourceTask = await vm.taskRepository!.Add();
                sourceTask.Title = "Roadmap DnD source";
                var targetTask = await vm.taskRepository.Add();
                targetTask.Title = "Roadmap DnD target";

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var sourceReady = WaitFor(() => FindRoadmapNode(graphControl!, sourceTask.Id) != null);
                var targetReady = WaitFor(() => FindRoadmapNode(graphControl!, targetTask.Id) != null);
                await Assert.That(sourceReady).IsTrue();
                await Assert.That(targetReady).IsTrue();

                var targetNode = WaitForTaskNode(graphControl!, targetTask.Id);
                using var dragData = DragDataFormats.CreateTransfer(GraphControl.CustomDataFormat, sourceTask);
                var dropArgs = new DragEventArgs(
                    DragDrop.DropEvent,
                    dragData,
                    targetNode,
                    new Avalonia.Point(targetNode.Bounds.Width / 2, targetNode.Bounds.Height / 2),
                    KeyModifiers.Control);

                await MainControl.Drop(view, dropArgs);
                await TestHelpers.WaitThrottleTime();

                await Assert.That(dropArgs.Handled).IsTrue();
                await Assert.That(dropArgs.DragEffects).IsEqualTo(DragDropEffects.Link);
                await Assert.That(sourceTask.Blocks).Contains(targetTask.Id);
                await Assert.That(targetTask.BlockedBy).Contains(sourceTask.Id);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapGraph_SelectedNodesDragDrop_AppliesBatchOperation()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                ((NotificationManagerWrapperMock)vm.ManagerWrapper).AskResult = true;

                var firstSource = await vm.taskRepository!.Add();
                firstSource.Title = "Roadmap batch DnD source A";
                var secondSource = await vm.taskRepository.Add();
                secondSource.Title = "Roadmap batch DnD source B";
                var targetTask = await vm.taskRepository.Add();
                targetTask.Title = "Roadmap batch DnD target";

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var firstReady = WaitFor(() => FindRoadmapNode(graphControl!, firstSource.Id) != null);
                var secondReady = WaitFor(() => FindRoadmapNode(graphControl!, secondSource.Id) != null);
                var targetReady = WaitFor(() => FindRoadmapNode(graphControl!, targetTask.Id) != null);
                await Assert.That(firstReady).IsTrue();
                await Assert.That(secondReady).IsTrue();
                await Assert.That(targetReady).IsTrue();

                var firstNode = WaitForTaskNode(graphControl!, firstSource.Id);
                var secondNode = WaitForTaskNode(graphControl, secondSource.Id);
                await ClickControlAsync(window, firstNode);
                await ClickControlAsync(window, secondNode, modifiers: RawInputModifiers.Control);
                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(new[]
                {
                    firstSource.Id,
                    secondSource.Id
                });

                var targetNode = WaitForTaskNode(graphControl, targetTask.Id);
                using var dragData = GraphControl.CreateRoadmapDragTransfer([firstSource, secondSource]);
                var dropArgs = new DragEventArgs(
                    DragDrop.DropEvent,
                    dragData,
                    targetNode,
                    new Avalonia.Point(targetNode.Bounds.Width / 2, targetNode.Bounds.Height / 2),
                    KeyModifiers.Control);

                await MainControl.Drop(view, dropArgs);
                await TestHelpers.WaitThrottleTime();

                var notificationManager = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                await Assert.That(dropArgs.Handled).IsTrue();
                await Assert.That(dropArgs.DragEffects).IsEqualTo(DragDropEffects.Link);
                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(firstSource.Blocks).Contains(targetTask.Id);
                await Assert.That(secondSource.Blocks).Contains(targetTask.Id);
                await Assert.That(targetTask.BlockedBy).Contains(firstSource.Id);
                await Assert.That(targetTask.BlockedBy).Contains(secondSource.Id);
                await Assert.That(GetSelectedRoadmapTaskIds(graphControl)).IsEquivalentTo(new[]
                {
                    firstSource.Id,
                    secondSource.Id
                });
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);
                await Assert.That(graphControl).IsNotNull();

                var graphText = WaitForTaskTitleTextBlock(
                    graphControl!,
                    MainWindowViewModelFixture.RootTask2Id);
                var targetNode = FindRoadmapNode(graphControl, MainWindowViewModelFixture.RootTask2Id);

                ApplyRoadmapClickSelectionForTest(
                    graphControl,
                    graphText,
                    targetTask,
                    KeyModifiers.None);
                await Assert.That(targetNode?.IsSelected).IsTrue();

                vm.Graph.Search.SearchText = targetTask.OnlyTextTitle;
                var highlighted = WaitFor(() => targetTask.IsHighlighted);

                await Assert.That(highlighted).IsTrue();
                await Assert.That(targetNode?.IsSelected).IsTrue();
                await Assert.That(graphText.FontWeight).IsEqualTo(FontWeight.Bold);
                await Assert.That(graphText.Foreground is ISolidColorBrush brush && brush.Color == Color.Parse("#2F80ED"))
                    .IsTrue();

                vm.Graph.Search.SearchText = "";
                var cleared = WaitFor(() => !targetTask.IsHighlighted);

                await Assert.That(cleared).IsTrue();
                await Assert.That(targetNode?.IsSelected).IsTrue();
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

    private static Window CreateWindow(Control content, double width, double height)
    {
        return new Window
        {
            Width = width,
            Height = height,
            Content = content
        };
    }

    private static async Task ClickControlAsync(
        Window window,
        Control control,
        MouseButton button = MouseButton.Left,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = GetControlCenterPoint(window, control);
        window.MouseDown(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static void PressRoadmapTitleSurface(Control titleSurface)
    {
        var pointer = new Pointer(1, PointerType.Mouse, true);
        var properties = new PointerPointProperties(
            RawInputModifiers.LeftMouseButton,
            PointerUpdateKind.LeftButtonPressed);
        var point = new Point(
            Math.Min(6, Math.Max(1, titleSurface.Bounds.Width / 2)),
            titleSurface.Bounds.Height / 2);

        titleSurface.RaiseEvent(new PointerPressedEventArgs(
            titleSurface,
            pointer,
            titleSurface,
            point,
            0,
            properties,
            KeyModifiers.None,
            1));
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressRoadmapNode(Control node, KeyModifiers modifiers, int clickCount)
    {
        var pointer = new Pointer(1, PointerType.Mouse, true);
        var properties = new PointerPointProperties(
            RawInputModifiers.LeftMouseButton,
            PointerUpdateKind.LeftButtonPressed);
        var point = new Point(node.Bounds.Width / 2, node.Bounds.Height / 2);

        node.RaiseEvent(new PointerPressedEventArgs(
            node,
            pointer,
            node,
            point,
            0,
            properties,
            modifiers,
            clickCount));
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task DragRoadmapRectangleAsync(
        Window window,
        Point start,
        Point end,
        RawInputModifiers modifiers)
    {
        window.MouseDown(start, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(end, modifiers | RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(end, MouseButton.Left, modifiers | RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static Point GetControlCenterPoint(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return point.Value;
    }

    private static Rect GetControlBounds(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(0, 0), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate bounds for control {control.GetType().Name}.");
        }

        return new Rect(point.Value.X, point.Value.Y, control.Bounds.Width, control.Bounds.Height);
    }

    private static Rect GetTransformedControlBounds(Visual relativeTo, Control control)
    {
        var topLeft = control.TranslatePoint(new Point(0, 0), relativeTo);
        var bottomRight = control.TranslatePoint(
            new Point(control.Bounds.Width, control.Bounds.Height),
            relativeTo);

        if (!topLeft.HasValue || !bottomRight.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate transformed bounds for control {control.GetType().Name}.");
        }

        return CreateNormalizedRect(topLeft.Value, bottomRight.Value);
    }

    private static IReadOnlyList<Border> FindRoadmapNodeBorders(Control root)
    {
        return root.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.DataContext is RoadmapNode)
            .ToArray();
    }

    private static Point FindEmptyEditorPoint(
        Window window,
        Control editor,
        IReadOnlyList<Border> nodeBorders)
    {
        var editorBounds = GetControlBounds(window, editor);
        var nodeBounds = nodeBorders
            .Select(node => GetControlBounds(window, node))
            .ToArray();
        var candidates = new[]
        {
            new Point(editorBounds.X + 8, editorBounds.Y + 8),
            new Point(editorBounds.Right - 8, editorBounds.Y + 8),
            new Point(editorBounds.X + 8, editorBounds.Bottom - 8),
            new Point(editorBounds.Right - 8, editorBounds.Bottom - 8),
            new Point(editorBounds.X + editorBounds.Width / 2, editorBounds.Y + 8)
        };

        foreach (var candidate in candidates)
        {
            if (editorBounds.Contains(candidate) &&
                nodeBounds.All(bounds => !bounds.Contains(candidate)))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not find an empty roadmap editor point for rectangle selection.");
    }

    private static Point FindRectangleEndPoint(
        Window window,
        Control editor,
        Point start,
        IReadOnlyList<Border> nodeBorders)
    {
        var editorBounds = GetControlBounds(window, editor);
        var nodeBounds = nodeBorders.Select(node => GetControlBounds(window, node)).ToArray();
        var union = UnionRects(nodeBounds);
        var unionCenterX = union.X + union.Width / 2;
        var unionCenterY = union.Y + union.Height / 2;
        var endX = start.X <= unionCenterX
            ? Math.Min(editorBounds.Right - 4, union.Right + 12)
            : Math.Max(editorBounds.X + 4, union.X - 12);
        var endY = start.Y <= unionCenterY
            ? Math.Min(editorBounds.Bottom - 4, union.Bottom + 12)
            : Math.Max(editorBounds.Y + 4, union.Y - 12);

        return new Point(endX, endY);
    }

    private static Rect UnionRects(IReadOnlyList<Rect> rectangles)
    {
        if (rectangles.Count == 0)
        {
            throw new ArgumentException("At least one rectangle is required.", nameof(rectangles));
        }

        var minX = rectangles.Min(rectangle => rectangle.X);
        var minY = rectangles.Min(rectangle => rectangle.Y);
        var maxX = rectangles.Max(rectangle => rectangle.Right);
        var maxY = rectangles.Max(rectangle => rectangle.Bottom);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static Rect CreateNormalizedRect(Point first, Point second)
    {
        var x = Math.Min(first.X, second.X);
        var y = Math.Min(first.Y, second.Y);
        return new Rect(
            x,
            y,
            Math.Abs(first.X - second.X),
            Math.Abs(first.Y - second.Y));
    }

    private static HashSet<string> GetRoadmapNodeIdsIntersectingWindowRect(
        Window window,
        Control root,
        Rect rectangle)
    {
        return FindRoadmapNodeBorders(root)
            .Where(border => RectanglesIntersect(GetControlBounds(window, border), rectangle))
            .Select(border => ((RoadmapNode)border.DataContext!).Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> GetRoadmapNodeIdsIntersectingTransformedWindowRect(
        Window window,
        Control root,
        Rect rectangle)
    {
        return FindRoadmapNodeBorders(root)
            .Where(border => RectanglesIntersect(GetTransformedControlBounds(window, border), rectangle))
            .Select(border => ((RoadmapNode)border.DataContext!).Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> GetSelectedRoadmapTaskIds(GraphControl graphControl)
    {
        return graphControl.RoadmapNodes
            .Where(node => node.IsSelected)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void ApplyRoadmapClickSelectionForTest(
        GraphControl graphControl,
        Control context,
        TaskItemViewModel task,
        KeyModifiers modifiers)
    {
        var method = typeof(GraphControl).GetMethod(
            "ApplyRoadmapClickSelection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (method == null)
        {
            throw new InvalidOperationException("GraphControl.ApplyRoadmapClickSelection was not found.");
        }

        method.Invoke(graphControl, new object?[] { context, task, modifiers });
        Dispatcher.UIThread.RunJobs();
    }

    private static IReadOnlyList<RoadmapNode> GetRoadmapNodesIntersectingForTest(
        GraphControl graphControl,
        Rect rectangle)
    {
        var method = typeof(GraphControl).GetMethod(
            "GetRoadmapNodesIntersecting",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (method == null)
        {
            throw new InvalidOperationException("GraphControl.GetRoadmapNodesIntersecting was not found.");
        }

        return (IReadOnlyList<RoadmapNode>)method.Invoke(graphControl, [rectangle])!;
    }

    private static bool RectanglesIntersect(Rect first, Rect second)
    {
        return first.X < second.Right &&
               second.X < first.Right &&
               first.Y < second.Bottom &&
               second.Y < first.Bottom;
    }

    private static void PressRoadmapHotkey(Window window, string hotkey)
    {
        switch (hotkey)
        {
            case "CtrlEnter":
                PressHotkey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);
                break;
            case "ShiftEnter":
                PressHotkey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Shift);
                break;
            case "CtrlTab":
                PressHotkey(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(hotkey), hotkey, "Unknown roadmap hotkey.");
        }
    }

    private static void PressHotkey(
        Window window,
        Key key,
        PhysicalKey physicalKey,
        RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
    }

    private static Button FindButtonForCommand(Control root, ICommand command)
    {
        return root.GetVisualDescendants()
            .OfType<Button>()
            .First(button => ReferenceEquals(button.Command, command));
    }

    private static TaskItemViewModel AddVisibleRoadmapFilterEmoji(MainWindowViewModel vm)
    {
        var task = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id)
            ?? throw new InvalidOperationException("Roadmap filter fixture task was not found.");
        task.Title = "\u274C " + task.TitleWithoutEmoji;

        return task;
    }

    private static GraphControl? WaitForGraphControl(Control root, int timeoutMilliseconds = 3000)
    {
        GraphControl? graphControl = null;
        var ready = WaitFor(() =>
        {
            graphControl = root as GraphControl ??
                root.GetVisualDescendants().OfType<GraphControl>().FirstOrDefault();
            return graphControl != null;
        }, timeoutMilliseconds);

        return ready ? graphControl : null;
    }

    private static GraphControl? OpenRoadmapTabAndWaitForGraphControl(
        MainControl root,
        int timeoutMilliseconds = 3000)
    {
        var roadmapTab = WaitForAutomationControl<TabItem>(
            root,
            "RoadmapTabItem",
            timeoutMilliseconds);

        roadmapTab.IsSelected = true;
        Dispatcher.UIThread.RunJobs();

        var graphControl = WaitForGraphControl(root, timeoutMilliseconds);
        if (graphControl != null &&
            WaitFor(() => graphControl.RoadmapNodes.Count > 0, timeoutMilliseconds))
        {
            WaitForStableRoadmapUpdates(graphControl, timeoutMilliseconds: timeoutMilliseconds);
        }

        return graphControl;
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
                    node.Id == taskId &&
                    candidate.GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Any(textBlock =>
                            textBlock.DataContext is TaskItemViewModel task &&
                            task.Id == taskId));

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

    private static Control WaitForRoadmapInlineTitleSurface(
        Control root,
        string taskId,
        int timeoutMilliseconds = 3000)
    {
        Control? surface = null;
        var ready = WaitFor(() =>
        {
            surface = root.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        "RoadmapInlineTaskTitleSurface",
                        StringComparison.Ordinal) &&
                    control.DataContext is TaskItemViewModel task &&
                    task.Id == taskId &&
                    control.IsAttachedToVisualTree() &&
                    control.IsVisible &&
                    control.IsEnabled);

            return surface != null;
        }, timeoutMilliseconds);

        if (!ready || surface == null)
        {
            throw new InvalidOperationException($"Roadmap inline title surface for task '{taskId}' was not found.");
        }

        return surface;
    }

    private static TextBox WaitForRoadmapInlineTitleEditor(
        Control root,
        string taskId,
        int timeoutMilliseconds = 3000)
    {
        TextBox? textBox = null;
        var ready = WaitFor(() =>
        {
            textBox = FindRoadmapInlineTitleEditor(root, taskId);
            return textBox != null;
        }, timeoutMilliseconds);

        if (!ready || textBox == null)
        {
            throw new InvalidOperationException($"Roadmap inline title TextBox for task '{taskId}' was not found.");
        }

        return textBox;
    }

    private static TextBox? FindRoadmapInlineTitleEditor(Control root, string taskId)
    {
        return root.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(control =>
                string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    "RoadmapInlineTaskTitleTextBox",
                    StringComparison.Ordinal) &&
                control.DataContext is TaskItemViewModel task &&
                task.Id == taskId &&
                control.IsAttachedToVisualTree() &&
                control.IsVisible &&
                control.IsEnabled);
    }

    private static async Task AssertRoadmapInlineTitleEditorHasNoFrame(TextBox inlineEditor)
    {
        await Assert.That(inlineEditor.BorderThickness).IsEqualTo(new Thickness(0));
        await Assert.That(inlineEditor.Padding).IsEqualTo(new Thickness(0));
        await Assert.That(IsTransparentBrush(inlineEditor.BorderBrush)).IsTrue();
        await Assert.That(IsTransparentBrush(inlineEditor.Background)).IsTrue();
        await Assert.That(inlineEditor.SelectedText).IsEqualTo(inlineEditor.Text);

        var templateBorder = FindRoadmapInlineTitleEditorTemplateBorder(inlineEditor);
        if (templateBorder == null)
        {
            throw new InvalidOperationException("Roadmap inline title editor template border was not found.");
        }

        await Assert.That(templateBorder.BorderThickness).IsEqualTo(new Thickness(0));
        await Assert.That(IsTransparentBrush(templateBorder.BorderBrush)).IsTrue();
        await Assert.That(IsTransparentBrush(templateBorder.Background)).IsTrue();
    }

    private static Border? FindRoadmapInlineTitleEditorTemplateBorder(TextBox inlineEditor)
    {
        return inlineEditor.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "PART_BorderElement", StringComparison.Ordinal));
    }

    private static bool IsTransparentBrush(IBrush? brush)
    {
        if (brush == null || brush.Opacity <= 0)
        {
            return true;
        }

        return brush is ISolidColorBrush solidColorBrush && solidColorBrush.Color.A == 0;
    }

    private static bool IsFocused(Window window, Control control)
    {
        return ReferenceEquals(window.FocusManager?.GetFocusedElement(), control) ||
               control.IsFocused;
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 3000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private static async Task<bool> WaitForAsync(
        Func<bool> predicate,
        int timeoutMilliseconds = 3000)
    {
        var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < timeoutAt)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate())
            {
                return true;
            }

            await Task.Delay(25);
        }

        Dispatcher.UIThread.RunJobs();
        return predicate();
    }

    private static async Task<bool> WaitForRoadmapUpdateAsync(
        GraphControl graphControl,
        int previousUpdateCount,
        int timeoutMilliseconds = 5000)
    {
        return await WaitForAsync(
            () => graphControl.RoadmapGraphUpdateCount > previousUpdateCount,
            timeoutMilliseconds);
    }

    private static bool IsVisibleAndArranged(Control control)
    {
        return control.IsVisible &&
               control.Bounds.Width > 0 &&
               control.Bounds.Height > 0;
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
