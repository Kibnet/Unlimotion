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
    private const double MinimumRowGap = RoadmapNode.Height + NodeSeparation;
    private const double RowInertiaWeight = 0.2;
    private const double EstimatedTextCharacterWidth = 7.4;
    private const double NodeChromeWidth = 54;
    private const double RepeaterMarkerWidth = 24;
    private const double EmojiWidth = 24;
    private const int BlocksEdgeWeight = 20;
    private const int ContainsEdgeWeight = 1;
    private const int LayerOrderingPasses = 24;
    private const int AdjacentSwapPasses = 4;
    private const int RowRelaxationPasses = 16;

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

        var visibleConnections = RemoveRedundantConnections(connections);
        var nodes = ApplySugiyamaLayout(nodesByTask.Values, visibleConnections, firstSeen);

        return new RoadmapGraphProjection(
            nodes,
            visibleConnections
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

    private static IReadOnlyList<ConnectionDefinition> RemoveRedundantConnections(
        IReadOnlyList<ConnectionDefinition> connections)
    {
        var outgoing = connections
            .GroupBy(connection => connection.Tail)
            .ToDictionary(group => group.Key, group => group.ToList());

        return connections
            .Where(connection => !HasAlternativePath(connection, outgoing))
            .ToList();
    }

    private static bool HasAlternativePath(
        ConnectionDefinition candidate,
        IReadOnlyDictionary<TaskItemViewModel, List<ConnectionDefinition>> outgoing)
    {
        if (!outgoing.TryGetValue(candidate.Tail, out var firstEdges))
        {
            return false;
        }

        var visited = new HashSet<TaskItemViewModel> { candidate.Tail };
        var queue = new Queue<TaskItemViewModel>();

        foreach (var edge in firstEdges)
        {
            if (edge.Equals(candidate) || edge.Head == candidate.Head)
            {
                continue;
            }

            if (visited.Add(edge.Head))
            {
                queue.Enqueue(edge.Head);
            }
        }

        while (queue.TryDequeue(out var task))
        {
            if (!outgoing.TryGetValue(task, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                if (edge.Equals(candidate))
                {
                    continue;
                }

                if (edge.Head == candidate.Head)
                {
                    return true;
                }

                if (visited.Add(edge.Head))
                {
                    queue.Enqueue(edge.Head);
                }
            }
        }

        return false;
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
            ApplyRoadmapLocations(nodes, connections, firstSeen);
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

    private static void ApplyRoadmapLocations(
        IReadOnlyList<RoadmapNode> nodes,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen)
    {
        var layers = BuildFallbackLayers(nodes.Select(node => node.TaskItem), connections, firstSeen);
        var layerOrder = nodes
            .GroupBy(node => layers.GetValueOrDefault(node.TaskItem))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(node => node.Location.Y)
                    .ThenBy(node => firstSeen[node.TaskItem])
                    .ToList());
        OptimizeLayerOrder(layerOrder, connections, firstSeen);
        var rows = BuildInitialRows(layerOrder);

        RelaxRows(nodes, connections, layerOrder, rows);
        NormalizeRows(rows);

        var layerX = BuildLayerPositions(layerOrder);
        foreach (var node in nodes)
        {
            node.Location = new Avalonia.Point(
                layerX[layers.GetValueOrDefault(node.TaskItem)],
                StartY + rows[node]);
        }
    }

    private static void OptimizeLayerOrder(
        Dictionary<int, List<RoadmapNode>> layerOrder,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen)
    {
        if (layerOrder.Count < 2 || connections.Count == 0)
        {
            return;
        }

        var nodesByTask = layerOrder
            .SelectMany(group => group.Value)
            .ToDictionary(node => node.TaskItem);
        var layersByTask = layerOrder
            .SelectMany(group => group.Value.Select(node => new { node.TaskItem, Layer = group.Key }))
            .ToDictionary(item => item.TaskItem, item => item.Layer);
        var layoutConnections = connections
            .Where(connection =>
                connection.Tail != connection.Head &&
                nodesByTask.ContainsKey(connection.Tail) &&
                nodesByTask.ContainsKey(connection.Head) &&
                layersByTask[connection.Tail] != layersByTask[connection.Head])
            .ToList();

        if (layoutConnections.Count == 0)
        {
            return;
        }

        var orderedLayers = layerOrder.Keys.OrderBy(layer => layer).ToArray();
        var orderByTask = BuildOrderByTask(layerOrder);

        for (var pass = 0; pass < LayerOrderingPasses; pass++)
        {
            foreach (var layerIndex in orderedLayers.Skip(1))
            {
                ReorderLayerByNeighborBarycenter(
                    layerOrder[layerIndex],
                    layoutConnections,
                    layersByTask,
                    orderByTask,
                    firstSeen,
                    preferLowerLayers: true);
                orderByTask = BuildOrderByTask(layerOrder);
            }

            foreach (var layerIndex in orderedLayers.Reverse().Skip(1))
            {
                ReorderLayerByNeighborBarycenter(
                    layerOrder[layerIndex],
                    layoutConnections,
                    layersByTask,
                    orderByTask,
                    firstSeen,
                    preferLowerLayers: false);
                orderByTask = BuildOrderByTask(layerOrder);
            }

            ApplyAdjacentCrossingSwaps(layerOrder, layoutConnections, orderByTask);
        }
    }

    private static Dictionary<TaskItemViewModel, int> BuildOrderByTask(
        IReadOnlyDictionary<int, List<RoadmapNode>> layerOrder)
    {
        return layerOrder
            .SelectMany(group => group.Value.Select((node, index) => new { node.TaskItem, Index = index }))
            .ToDictionary(item => item.TaskItem, item => item.Index);
    }

    private static void ReorderLayerByNeighborBarycenter(
        List<RoadmapNode> layer,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> layersByTask,
        IReadOnlyDictionary<TaskItemViewModel, int> orderByTask,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen,
        bool preferLowerLayers)
    {
        if (layer.Count < 2)
        {
            return;
        }

        var desiredOrderByTask = new Dictionary<TaskItemViewModel, WeightedOrder>();
        var currentLayerByTask = layer.ToDictionary(node => node.TaskItem, node => layersByTask[node.TaskItem]);

        foreach (var connection in connections)
        {
            AddNeighbor(connection.Tail, connection.Head, connection.Kind);
            AddNeighbor(connection.Head, connection.Tail, connection.Kind);
        }

        layer.Sort((left, right) =>
        {
            var leftOrder = desiredOrderByTask.TryGetValue(left.TaskItem, out var leftWeightedOrder)
                ? leftWeightedOrder.Value
                : orderByTask[left.TaskItem];
            var rightOrder = desiredOrderByTask.TryGetValue(right.TaskItem, out var rightWeightedOrder)
                ? rightWeightedOrder.Value
                : orderByTask[right.TaskItem];
            var orderComparison = leftOrder.CompareTo(rightOrder);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            var currentOrderComparison = orderByTask[left.TaskItem].CompareTo(orderByTask[right.TaskItem]);
            return currentOrderComparison != 0
                ? currentOrderComparison
                : firstSeen[left.TaskItem].CompareTo(firstSeen[right.TaskItem]);
        });

        void AddNeighbor(
            TaskItemViewModel task,
            TaskItemViewModel neighbor,
            RoadmapConnectionKind kind)
        {
            if (!currentLayerByTask.TryGetValue(task, out var taskLayer) ||
                !layersByTask.TryGetValue(neighbor, out var neighborLayer))
            {
                return;
            }

            if (preferLowerLayers != neighborLayer < taskLayer)
            {
                return;
            }

            var weight = GetConnectionWeight(kind);
            var weightedOrder = desiredOrderByTask.GetValueOrDefault(task);
            desiredOrderByTask[task] = new WeightedOrder(
                weightedOrder.WeightedSum + orderByTask[neighbor] * weight,
                weightedOrder.Weight + weight);
        }
    }

    private static void ApplyAdjacentCrossingSwaps(
        IReadOnlyDictionary<int, List<RoadmapNode>> layerOrder,
        IReadOnlyList<ConnectionDefinition> connections,
        Dictionary<TaskItemViewModel, int> orderByTask)
    {
        var connectionsByTask = connections
            .SelectMany(connection => new[]
            {
                new { Task = connection.Tail, Connection = connection },
                new { Task = connection.Head, Connection = connection }
            })
            .GroupBy(item => item.Task)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Connection).Distinct().ToList());

        for (var pass = 0; pass < AdjacentSwapPasses; pass++)
        {
            var improved = false;

            foreach (var layer in layerOrder.OrderBy(group => group.Key).Select(group => group.Value))
            {
                if (layer.Count < 2)
                {
                    continue;
                }

                for (var index = 0; index < layer.Count - 1; index++)
                {
                    var first = layer[index];
                    var second = layer[index + 1];
                    var affectedConnections = GetAffectedConnections(first.TaskItem, second.TaskItem);
                    var before = CountAffectedCrossings(affectedConnections, connections, orderByTask);

                    layer[index] = second;
                    layer[index + 1] = first;
                    orderByTask[first.TaskItem] = index + 1;
                    orderByTask[second.TaskItem] = index;

                    var after = CountAffectedCrossings(affectedConnections, connections, orderByTask);
                    if (after < before)
                    {
                        improved = true;
                        continue;
                    }

                    layer[index] = first;
                    layer[index + 1] = second;
                    orderByTask[first.TaskItem] = index;
                    orderByTask[second.TaskItem] = index + 1;
                }
            }

            if (!improved)
            {
                return;
            }
        }

        List<ConnectionDefinition> GetAffectedConnections(
            TaskItemViewModel first,
            TaskItemViewModel second)
        {
            var affected = new HashSet<ConnectionDefinition>();
            if (connectionsByTask.TryGetValue(first, out var firstConnections))
            {
                affected.UnionWith(firstConnections);
            }

            if (connectionsByTask.TryGetValue(second, out var secondConnections))
            {
                affected.UnionWith(secondConnections);
            }

            return affected.ToList();
        }
    }

    private static long CountAffectedCrossings(
        IReadOnlyList<ConnectionDefinition> affectedConnections,
        IReadOnlyList<ConnectionDefinition> allConnections,
        IReadOnlyDictionary<TaskItemViewModel, int> orderByTask)
    {
        long crossings = 0;

        foreach (var affectedConnection in affectedConnections)
        {
            foreach (var connection in allConnections)
            {
                if (affectedConnection.Equals(connection) ||
                    !HasOrderCrossing(affectedConnection, connection, orderByTask))
                {
                    continue;
                }

                crossings += GetConnectionWeight(affectedConnection.Kind) * GetConnectionWeight(connection.Kind);
            }
        }

        return crossings;
    }

    private static bool HasOrderCrossing(
        ConnectionDefinition first,
        ConnectionDefinition second,
        IReadOnlyDictionary<TaskItemViewModel, int> orderByTask)
    {
        var sourceOrder = orderByTask[first.Tail].CompareTo(orderByTask[second.Tail]);
        var targetOrder = orderByTask[first.Head].CompareTo(orderByTask[second.Head]);

        return sourceOrder != 0 &&
               targetOrder != 0 &&
               sourceOrder != targetOrder;
    }

    private static Dictionary<RoadmapNode, double> BuildInitialRows(
        IReadOnlyDictionary<int, List<RoadmapNode>> layerOrder)
    {
        var rows = new Dictionary<RoadmapNode, double>();

        foreach (var layer in layerOrder.Values)
        {
            for (var index = 0; index < layer.Count; index++)
            {
                rows[layer[index]] = index * MinimumRowGap;
            }
        }

        return rows;
    }

    private static Dictionary<int, double> BuildLayerPositions(
        IReadOnlyDictionary<int, List<RoadmapNode>> layerOrder)
    {
        var positions = new Dictionary<int, double>();
        var maxLayer = layerOrder.Keys.DefaultIfEmpty(0).Max();
        var x = StartX;

        for (var layer = 0; layer <= maxLayer; layer++)
        {
            positions[layer] = x;
            var layerWidth = layerOrder.TryGetValue(layer, out var nodes) && nodes.Count > 0
                ? nodes.Max(node => node.Width)
                : RoadmapNode.MinWidth;

            x += layerWidth + LayerSeparation;
        }

        return positions;
    }

    private static void RelaxRows(
        IReadOnlyList<RoadmapNode> nodes,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<int, List<RoadmapNode>> layerOrder,
        Dictionary<RoadmapNode, double> rows)
    {
        var nodesByTask = nodes.ToDictionary(node => node.TaskItem);
        var neighbors = nodes.ToDictionary(node => node, _ => new List<WeightedNodeNeighbor>());

        foreach (var connection in connections)
        {
            if (!nodesByTask.TryGetValue(connection.Tail, out var tail) ||
                !nodesByTask.TryGetValue(connection.Head, out var head))
            {
                continue;
            }

            var weight = GetConnectionWeight(connection.Kind);
            neighbors[tail].Add(new WeightedNodeNeighbor(head, weight));
            neighbors[head].Add(new WeightedNodeNeighbor(tail, weight));
        }

        if (connections.Count == 0 || layerOrder.Count < 2)
        {
            return;
        }

        var orderedLayers = layerOrder.Keys.OrderBy(layer => layer).ToArray();
        for (var pass = 0; pass < RowRelaxationPasses; pass++)
        {
            foreach (var layer in orderedLayers)
            {
                RelaxLayer(layer);
            }

            foreach (var layer in orderedLayers.Reverse())
            {
                RelaxLayer(layer);
            }

            NormalizeRows(rows);
        }

        void RelaxLayer(int layerIndex)
        {
            if (!layerOrder.TryGetValue(layerIndex, out var layer) || layer.Count == 0)
            {
                return;
            }

            var desiredRows = new double[layer.Count];
            var desiredWeights = new double[layer.Count];

            for (var index = 0; index < layer.Count; index++)
            {
                var node = layer[index];
                var weightedRowSum = rows[node] * RowInertiaWeight;
                var totalWeight = RowInertiaWeight;

                foreach (var neighbor in neighbors[node])
                {
                    weightedRowSum += rows[neighbor.Node] * neighbor.Weight;
                    totalWeight += neighbor.Weight;
                }

                desiredRows[index] = weightedRowSum / totalWeight;
                desiredWeights[index] = totalWeight;
            }

            var adjustedRows = ProjectRowsPreservingOrder(
                desiredRows,
                desiredWeights,
                MinimumRowGap);

            for (var index = 0; index < layer.Count; index++)
            {
                rows[layer[index]] = adjustedRows[index];
            }
        }
    }

    private static double[] ProjectRowsPreservingOrder(
        IReadOnlyList<double> desiredRows,
        IReadOnlyList<double> desiredWeights,
        double minimumGap)
    {
        if (desiredRows.Count == 0)
        {
            return Array.Empty<double>();
        }

        var blocks = new List<RowBlock>();
        for (var index = 0; index < desiredRows.Count; index++)
        {
            blocks.Add(new RowBlock(
                index,
                index,
                Math.Max(desiredWeights[index], 0.0001),
                desiredRows[index] - index * minimumGap));

            while (blocks.Count >= 2 && blocks[^2].Value > blocks[^1].Value)
            {
                var current = blocks[^1];
                var previous = blocks[^2];
                var weight = previous.Weight + current.Weight;
                var value = (previous.Value * previous.Weight + current.Value * current.Weight) / weight;

                blocks[^2] = new RowBlock(previous.Start, current.End, weight, value);
                blocks.RemoveAt(blocks.Count - 1);
            }
        }

        var result = new double[desiredRows.Count];
        foreach (var block in blocks)
        {
            for (var index = block.Start; index <= block.End; index++)
            {
                result[index] = block.Value + index * minimumGap;
            }
        }

        return result;
    }

    private static void NormalizeRows(Dictionary<RoadmapNode, double> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var minRow = rows.Values.Min();
        if (Math.Abs(minRow) < 0.0001)
        {
            return;
        }

        foreach (var node in rows.Keys.ToArray())
        {
            rows[node] -= minRow;
        }
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

    private readonly record struct WeightedNodeNeighbor(
        RoadmapNode Node,
        int Weight);

    private readonly record struct WeightedOrder(
        double WeightedSum,
        double Weight)
    {
        public double Value => Weight <= 0 ? 0 : WeightedSum / Weight;
    }

    private readonly record struct RowBlock(
        int Start,
        int End,
        double Weight,
        double Value);
}
