using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using Unlimotion.ViewModel;
using MsaglRectangle = Microsoft.Msagl.Core.Geometry.Rectangle;
using MsaglDrawing = Microsoft.Msagl.Drawing;

namespace Unlimotion.Views.Graph;

public static class RoadmapGraphBuilder
{
    private const double StartX = 24;
    private const double StartY = 24;
    private const double NodeSeparation = 10;
    private const double LayerSeparation = 140;
    private const double EstimatedTextCharacterWidth = 7.4;
    private const double NodeChromeWidth = 54;
    private const double RepeaterMarkerWidth = 24;
    private const double EmojiWidth = 24;
    private const int BlocksEdgeWeight = 20;
    private const int ContainsEdgeWeight = 1;

    public static RoadmapGraphProjection Build(
        ReadOnlyObservableCollection<TaskWrapperViewModel> roots,
        IReadOnlyDictionary<string, double>? measuredNodeWidths = null)
    {
        var nodesByTask = new Dictionary<TaskItemViewModel, RoadmapNode>();
        var firstSeen = new Dictionary<TaskItemViewModel, int>();
        var processed = new HashSet<TaskItemViewModel>();
        var queue = new Queue<TaskWrapperViewModel>();
        var connections = new List<ConnectionDefinition>();
        var connectionKeys = new HashSet<string>();
        var nextSeenIndex = 0;

        foreach (var root in roots)
        {
            queue.Enqueue(root);
            EnsureNode(root.TaskItem);
        }

        while (queue.TryDequeue(out var task))
        {
            var taskItem = task.TaskItem;
            EnsureNode(taskItem);

            if (!processed.Add(taskItem))
            {
                continue;
            }

            var containsTaskIds = task.SubTasks.Select(e => e.TaskItem.Id).ToArray();

            foreach (var containsTask in task.SubTasks)
            {
                var child = containsTask.TaskItem;
                EnsureNode(child);

                var childBlocksAnotherChild = child.Blocks.Any(item =>
                    containsTaskIds.Where(id => id != child.Id).Contains(item));
                var hasChildBlocksBlocker = child.Blocks.Any(item => taskItem.BlockedBy.Contains(item));

                if (!hasChildBlocksBlocker && !childBlocksAnotherChild)
                {
                    AddConnection(child, taskItem, RoadmapConnectionKind.Contains);
                }

                if (!processed.Contains(child))
                {
                    queue.Enqueue(containsTask);
                }
            }

            foreach (var blockedTask in taskItem.BlocksTasks)
            {
                EnsureNode(blockedTask);
                AddConnection(taskItem, blockedTask, RoadmapConnectionKind.Blocks);
            }
        }

        var nodes = ApplySugiyamaLayout(nodesByTask.Values, connections, firstSeen);

        return new RoadmapGraphProjection(
            nodes,
            connections
                .Select(connection => new RoadmapConnection(
                    nodesByTask[connection.Tail],
                    nodesByTask[connection.Head],
                    connection.Kind))
                .ToList());

        void EnsureNode(TaskItemViewModel taskItem)
        {
            if (nodesByTask.ContainsKey(taskItem))
            {
                return;
            }

            var node = new RoadmapNode(taskItem);
            node.SetMeasuredWidth(ResolveNodeWidth(taskItem));
            nodesByTask.Add(taskItem, node);
            firstSeen.Add(taskItem, nextSeenIndex++);
        }

        double ResolveNodeWidth(TaskItemViewModel taskItem)
        {
            if (measuredNodeWidths != null &&
                measuredNodeWidths.TryGetValue(taskItem.Id, out var measuredWidth))
            {
                return measuredWidth;
            }

            return EstimateNodeWidth(taskItem);
        }

        void AddConnection(TaskItemViewModel tail, TaskItemViewModel head, RoadmapConnectionKind kind)
        {
            var key = $"{kind}:{tail.Id}->{head.Id}";
            if (connectionKeys.Add(key))
            {
                connections.Add(new ConnectionDefinition(tail, head, kind));
            }
        }
    }

    private static List<RoadmapNode> ApplySugiyamaLayout(
        IEnumerable<RoadmapNode> sourceNodes,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen)
    {
        var nodes = sourceNodes.ToList();
        if (nodes.Count == 0)
        {
            return nodes;
        }

        var graph = new MsaglDrawing.Graph
        {
            LayoutAlgorithmSettings = CreateSugiyamaLayoutSettings()
        };
        graph.Attr.LayerDirection = MsaglDrawing.LayerDirection.LR;

        var nodeIds = nodes.ToDictionary(
            node => node.TaskItem,
            node => "n" + firstSeen[node.TaskItem].ToString(CultureInfo.InvariantCulture));
        var drawingNodesByTask = new Dictionary<TaskItemViewModel, MsaglDrawing.Node>();

        foreach (var node in nodes.OrderBy(node => firstSeen[node.TaskItem]))
        {
            drawingNodesByTask[node.TaskItem] = graph.AddNode(nodeIds[node.TaskItem]);
        }

        var drawingEdges = new List<(MsaglDrawing.Edge Edge, RoadmapConnectionKind Kind)>();
        foreach (var connection in connections)
        {
            if (connection.Tail == connection.Head ||
                !nodeIds.TryGetValue(connection.Tail, out var tailId) ||
                !nodeIds.TryGetValue(connection.Head, out var headId))
            {
                continue;
            }

            drawingEdges.Add((graph.AddEdge(tailId, headId), connection.Kind));
        }

        try
        {
            graph.CreateGeometryGraph();

            foreach (var node in nodes)
            {
                var drawingNode = drawingNodesByTask[node.TaskItem];
                drawingNode.GeometryNode.BoundaryCurve = CurveFactory.CreateRectangle(
                    node.Width,
                    RoadmapNode.Height,
                    new Microsoft.Msagl.Core.Geometry.Point());
            }

            foreach (var (edge, kind) in drawingEdges)
            {
                if (edge.GeometryEdge != null)
                {
                    edge.GeometryEdge.Weight = GetConnectionWeight(kind);
                }
            }

            LayoutHelpers.CalculateLayout(
                graph.GeometryGraph,
                graph.LayoutAlgorithmSettings,
                null);

            ApplyMsaglLocations(nodes, drawingNodesByTask, graph.GeometryGraph.BoundingBox);
        }
        catch (Exception exception)
        {
            Trace.TraceError("MSAGL Sugiyama roadmap layout failed: {0}", exception);
            ApplyFallbackLayout(nodes, connections, firstSeen);
        }

        return nodes
            .OrderBy(node => node.Location.X)
            .ThenBy(node => node.Location.Y)
            .ThenBy(node => firstSeen[node.TaskItem])
            .ToList();
    }

    private static SugiyamaLayoutSettings CreateSugiyamaLayoutSettings()
    {
        return new SugiyamaLayoutSettings
        {
            LayerSeparation = LayerSeparation,
            NodeSeparation = NodeSeparation,
            MinNodeWidth = RoadmapNode.MinWidth,
            MinNodeHeight = RoadmapNode.Height,
            RandomSeedForOrdering = 0,
            MaxNumberOfPassesInOrdering = 24,
            NoGainAdjacentSwapStepsBound = 8,
            RepetitionCoefficientForOrdering = 8
        };
    }

    private static void ApplyMsaglLocations(
        IEnumerable<RoadmapNode> nodes,
        IReadOnlyDictionary<TaskItemViewModel, MsaglDrawing.Node> drawingNodesByTask,
        MsaglRectangle graphBounds)
    {
        foreach (var node in nodes)
        {
            var nodeBounds = drawingNodesByTask[node.TaskItem].GeometryNode.BoundingBox;
            node.Location = new Avalonia.Point(
                StartX + nodeBounds.Left - graphBounds.Left,
                StartY + graphBounds.Top - nodeBounds.Top);
        }
    }

    private static void ApplyFallbackLayout(
        IReadOnlyList<RoadmapNode> nodes,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen)
    {
        var layers = BuildFallbackLayers(nodes.Select(node => node.TaskItem), connections, firstSeen);
        var rowsByLayer = nodes
            .GroupBy(node => layers.GetValueOrDefault(node.TaskItem))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(node => firstSeen[node.TaskItem])
                    .Select((node, index) => new { node, index })
                    .ToDictionary(item => item.node.TaskItem, item => item.index));

        foreach (var node in nodes)
        {
            var layer = layers.GetValueOrDefault(node.TaskItem);
            var row = rowsByLayer[layer][node.TaskItem];
            node.Location = new Avalonia.Point(
                StartX + layer * (RoadmapNode.MaxWidth + LayerSeparation),
                StartY + row * (RoadmapNode.Height + NodeSeparation));
        }
    }

    private static Dictionary<TaskItemViewModel, int> BuildFallbackLayers(
        IEnumerable<TaskItemViewModel> tasks,
        IEnumerable<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen)
    {
        var nodes = tasks.ToList();
        var layers = nodes.ToDictionary(task => task, _ => 0);
        var outgoing = nodes.ToDictionary(task => task, _ => new List<TaskItemViewModel>());
        var incomingCount = nodes.ToDictionary(task => task, _ => 0);

        foreach (var connection in connections)
        {
            if (connection.Tail == connection.Head ||
                !outgoing.ContainsKey(connection.Tail) ||
                !incomingCount.ContainsKey(connection.Head))
            {
                continue;
            }

            outgoing[connection.Tail].Add(connection.Head);
            incomingCount[connection.Head]++;
        }

        var queue = new Queue<TaskItemViewModel>(nodes
            .Where(task => incomingCount[task] == 0)
            .OrderBy(task => firstSeen[task]));
        var processed = new HashSet<TaskItemViewModel>();

        while (queue.TryDequeue(out var task))
        {
            if (!processed.Add(task))
            {
                continue;
            }

            foreach (var head in outgoing[task].OrderBy(item => firstSeen[item]))
            {
                layers[head] = Math.Max(layers[head], layers[task] + 1);
                incomingCount[head]--;
                if (incomingCount[head] == 0)
                {
                    queue.Enqueue(head);
                }
            }
        }

        return layers;
    }

    private static double EstimateNodeWidth(TaskItemViewModel taskItem)
    {
        var title = taskItem.TitleWithoutEmoji ?? taskItem.OnlyTextTitle ?? taskItem.Title ?? string.Empty;
        var longestLineLength = title
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .DefaultIfEmpty(title)
            .Max(line => line.Length);
        var textWidth = Math.Min(300, longestLineLength * EstimatedTextCharacterWidth);
        var emojiWidth = string.IsNullOrWhiteSpace(taskItem.GetAllEmoji) ? 0 : EmojiWidth;
        var repeaterWidth = taskItem.IsHaveRepeater ? RepeaterMarkerWidth : 0;

        return Math.Clamp(
            Math.Ceiling(NodeChromeWidth + emojiWidth + repeaterWidth + textWidth),
            RoadmapNode.MinWidth,
            RoadmapNode.MaxWidth);
    }

    private static int GetConnectionWeight(RoadmapConnectionKind kind)
    {
        return kind == RoadmapConnectionKind.Blocks ? BlocksEdgeWeight : ContainsEdgeWeight;
    }

    private readonly record struct ConnectionDefinition(
        TaskItemViewModel Tail,
        TaskItemViewModel Head,
        RoadmapConnectionKind Kind);
}
