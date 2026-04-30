using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Unlimotion.ViewModel;

namespace Unlimotion.Views.Graph;

public static class RoadmapGraphBuilder
{
    private const double StartX = 24;
    private const double StartY = 24;
    private const double HorizontalSpacing = 520;
    private const double VerticalSpacing = 92;
    private const double RootSpacingRows = 0.75;
    private const int CrossingMinimizationPasses = 4;
    private const int AdjacentTransposePasses = 3;
    private const int EdgeLengthMinimizationPasses = 6;
    private const double MinimumRowGap = 0.75;
    private const double RowInertiaWeight = 0.35;
    private const double ContainsEdgeWeight = 1;
    private const double BlocksEdgeWeight = 4;

    public static RoadmapGraphProjection Build(ReadOnlyObservableCollection<TaskWrapperViewModel> roots)
    {
        var nodesByTask = new Dictionary<TaskItemViewModel, RoadmapNode>();
        var firstSeen = new Dictionary<TaskItemViewModel, int>();
        var processed = new HashSet<TaskItemViewModel>();
        var queue = new Queue<TaskWrapperViewModel>();
        var connections = new List<ConnectionDefinition>();
        var connectionKeys = new HashSet<string>();
        var containmentChildren = new Dictionary<TaskItemViewModel, List<TaskItemViewModel>>();
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
                    AddContainmentChild(taskItem, child);
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

        var depths = BuildLayers(nodesByTask.Keys, connections, firstSeen);
        depths = CompactLayerSpans(nodesByTask.Keys, connections, depths);
        var rows = BuildRows(nodesByTask.Keys, containmentChildren, depths, firstSeen);
        rows = MinimizeCrossings(nodesByTask.Keys, connections, depths, rows, firstSeen);

        var nodes = nodesByTask.Values
            .OrderBy(node => depths.GetValueOrDefault(node.TaskItem))
            .ThenBy(node => rows.GetValueOrDefault(node.TaskItem))
            .ThenBy(node => firstSeen[node.TaskItem])
            .ToList();

        ApplyLayout(nodes, depths, rows);

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
            if (!nodesByTask.ContainsKey(taskItem))
            {
                nodesByTask.Add(taskItem, new RoadmapNode(taskItem));
                firstSeen.Add(taskItem, nextSeenIndex++);
            }
        }

        void AddConnection(TaskItemViewModel tail, TaskItemViewModel head, RoadmapConnectionKind kind)
        {
            var key = $"{kind}:{tail.Id}->{head.Id}";
            if (connectionKeys.Add(key))
            {
                connections.Add(new ConnectionDefinition(tail, head, kind));
            }
        }

        void AddContainmentChild(TaskItemViewModel parent, TaskItemViewModel child)
        {
            if (!containmentChildren.TryGetValue(parent, out var children))
            {
                children = new List<TaskItemViewModel>();
                containmentChildren.Add(parent, children);
            }

            if (!children.Contains(child))
            {
                children.Add(child);
            }
        }
    }

    private static Dictionary<TaskItemViewModel, int> BuildLayers(
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

        foreach (var task in nodes.Where(task => !processed.Contains(task)).OrderBy(task => firstSeen[task]))
        {
            var incomingLayers = connections
                .Where(connection => connection.Head == task && layers.ContainsKey(connection.Tail))
                .Select(connection => layers[connection.Tail] + 1);

            layers[task] = Math.Max(layers[task], incomingLayers.DefaultIfEmpty(0).Max());
        }

        return layers;
    }

    private static Dictionary<TaskItemViewModel, int> CompactLayerSpans(
        IEnumerable<TaskItemViewModel> tasks,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> initialLayers)
    {
        var nodes = tasks.ToList();
        var layers = initialLayers.ToDictionary(pair => pair.Key, pair => pair.Value);
        var outgoing = nodes.ToDictionary(task => task, _ => new List<TaskItemViewModel>());

        foreach (var connection in connections)
        {
            if (connection.Tail == connection.Head ||
                !outgoing.ContainsKey(connection.Tail) ||
                !layers.ContainsKey(connection.Head))
            {
                continue;
            }

            outgoing[connection.Tail].Add(connection.Head);
        }

        for (var pass = 0; pass < nodes.Count; pass++)
        {
            var changed = false;
            foreach (var task in nodes.OrderByDescending(task => layers.GetValueOrDefault(task)))
            {
                if (!outgoing.TryGetValue(task, out var heads) || heads.Count == 0)
                {
                    continue;
                }

                var nearestAllowedLayer = heads
                    .Where(layers.ContainsKey)
                    .Select(head => layers[head] - 1)
                    .DefaultIfEmpty(layers[task])
                    .Min();

                if (nearestAllowedLayer <= layers[task])
                {
                    continue;
                }

                layers[task] = nearestAllowedLayer;
                changed = true;
            }

            if (!changed)
            {
                break;
            }
        }

        var minLayer = layers.Values.DefaultIfEmpty(0).Min();
        if (minLayer != 0)
        {
            foreach (var task in layers.Keys.ToArray())
            {
                layers[task] -= minLayer;
            }
        }

        return layers;
    }

    private static Dictionary<TaskItemViewModel, double> BuildRows(
        IEnumerable<TaskItemViewModel> tasks,
        IReadOnlyDictionary<TaskItemViewModel, List<TaskItemViewModel>> containmentChildren,
        IReadOnlyDictionary<TaskItemViewModel, int> depths,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen)
    {
        var nodes = tasks.ToList();
        var nodeSet = nodes.ToHashSet();
        var rows = new Dictionary<TaskItemViewModel, double>();
        var visiting = new HashSet<TaskItemViewModel>();
        var contained = containmentChildren
            .SelectMany(pair => pair.Value)
            .Where(nodeSet.Contains)
            .ToHashSet();
        var nextRow = 0d;

        foreach (var root in nodes
                     .Where(task => !contained.Contains(task))
                     .OrderBy(task => firstSeen[task]))
        {
            if (!rows.ContainsKey(root))
            {
                AssignRow(root);
                nextRow += RootSpacingRows;
            }
        }

        foreach (var task in nodes
                     .Where(task => !rows.ContainsKey(task))
                     .OrderBy(task => depths.GetValueOrDefault(task))
                     .ThenBy(task => firstSeen[task]))
        {
            rows[task] = nextRow++;
            nextRow += RootSpacingRows;
        }

        return rows;

        double AssignRow(TaskItemViewModel task)
        {
            if (rows.TryGetValue(task, out var existingRow))
            {
                return existingRow;
            }

            if (!visiting.Add(task))
            {
                return nextRow++;
            }

            var children = containmentChildren.TryGetValue(task, out var taskChildren)
                ? taskChildren.Where(nodeSet.Contains).OrderBy(child => firstSeen[child]).ToList()
                : new List<TaskItemViewModel>();

            var childRows = children.Select(AssignRow).ToList();
            var row = childRows.Count == 0
                ? nextRow++
                : (childRows.Min() + childRows.Max()) / 2;

            rows[task] = row;
            visiting.Remove(task);
            return row;
        }
    }

    private static Dictionary<TaskItemViewModel, double> MinimizeCrossings(
        IEnumerable<TaskItemViewModel> tasks,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> depths,
        IReadOnlyDictionary<TaskItemViewModel, double> initialRows,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen)
    {
        var layout = BuildSegmentedLayoutGraph(tasks, connections, depths, initialRows, firstSeen);
        var layerOrder = layout.LayerOrder;

        if (layerOrder.Count < 2)
        {
            return initialRows.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        var minDepth = layerOrder.Keys.Min();
        var maxDepth = layerOrder.Keys.Max();

        for (var pass = 0; pass < CrossingMinimizationPasses; pass++)
        {
            for (var depth = minDepth + 1; depth <= maxDepth; depth++)
            {
                ReorderLayer(depth, useIncoming: true);
                ImproveLayerByAdjacentTransposes(layerOrder, layout.Segments, depth);
            }

            for (var depth = maxDepth - 1; depth >= minDepth; depth--)
            {
                ReorderLayer(depth, useIncoming: false);
                ImproveLayerByAdjacentTransposes(layerOrder, layout.Segments, depth);
            }
        }

        var vertexRows = AssignRowsPreservingLayerOrder(layerOrder);
        vertexRows = MinimizeSegmentLengths(layout, vertexRows);

        var result = layout.RealVertices.ToDictionary(
            pair => pair.Key,
            pair => vertexRows.GetValueOrDefault(pair.Value, pair.Value.InitialRow));
        NormalizeRows(result);

        return result;

        void ReorderLayer(int depth, bool useIncoming)
        {
            if (!layerOrder.TryGetValue(depth, out var layer) || layer.Count <= 1)
            {
                return;
            }

            var positions = BuildCurrentPositions();
            layerOrder[depth] = layer
                .Select((vertex, index) => new
                {
                    Vertex = vertex,
                    CurrentIndex = index,
                    Weight = GetBarycenter(vertex, useIncoming, positions)
                })
                .OrderBy(item => item.Weight)
                .ThenBy(item => item.CurrentIndex)
                .ThenBy(item => item.Vertex.StableIndex)
                .Select(item => item.Vertex)
                .ToList();
        }

        Dictionary<LayoutVertex, double> BuildCurrentPositions()
        {
            return layerOrder
                .SelectMany(pair => pair.Value.Select((vertex, index) => new { vertex, index }))
                .ToDictionary(item => item.vertex, item => (double)item.index);
        }

        double GetBarycenter(
            LayoutVertex vertex,
            bool useIncoming,
            IReadOnlyDictionary<LayoutVertex, double> positions)
        {
            var weightedRows = layout.Segments
                .Where(segment => useIncoming ? ReferenceEquals(segment.Head, vertex) : ReferenceEquals(segment.Tail, vertex))
                .Select(segment => new
                {
                    Neighbor = useIncoming ? segment.Tail : segment.Head,
                    Weight = GetConnectionWeight(segment.Kind)
                })
                .Where(item => positions.ContainsKey(item.Neighbor))
                .ToArray();

            if (weightedRows.Length == 0)
            {
                return positions[vertex];
            }

            var totalWeight = weightedRows.Sum(item => item.Weight);
            return weightedRows.Sum(item => positions[item.Neighbor] * item.Weight) / totalWeight;
        }
    }

    private static SegmentedLayoutGraph BuildSegmentedLayoutGraph(
        IEnumerable<TaskItemViewModel> tasks,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> depths,
        IReadOnlyDictionary<TaskItemViewModel, double> initialRows,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen)
    {
        var nodes = tasks.ToList();
        var nextStableIndex = nodes.Count;
        var realVertices = nodes.ToDictionary(
            task => task,
            task => new LayoutVertex(
                task,
                depths.GetValueOrDefault(task),
                initialRows.GetValueOrDefault(task),
                firstSeen.GetValueOrDefault(task)));
        var layerOrder = realVertices.Values
            .GroupBy(vertex => vertex.Layer)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(vertex => vertex.InitialRow)
                    .ThenBy(vertex => vertex.StableIndex)
                    .ToList());
        var segments = new List<LayoutSegment>();

        foreach (var connection in connections)
        {
            if (!realVertices.TryGetValue(connection.Tail, out var tail) ||
                !realVertices.TryGetValue(connection.Head, out var head))
            {
                continue;
            }

            var span = head.Layer - tail.Layer;
            if (span <= 1)
            {
                segments.Add(new LayoutSegment(tail, head, connection.Kind));
                continue;
            }

            var previous = tail;
            for (var layer = tail.Layer + 1; layer < head.Layer; layer++)
            {
                var ratio = (double)(layer - tail.Layer) / span;
                var dummy = new LayoutVertex(
                    null,
                    layer,
                    Lerp(tail.InitialRow, head.InitialRow, ratio),
                    nextStableIndex++);

                if (!layerOrder.TryGetValue(layer, out var layerVertices))
                {
                    layerVertices = new List<LayoutVertex>();
                    layerOrder.Add(layer, layerVertices);
                }

                layerVertices.Add(dummy);
                segments.Add(new LayoutSegment(previous, dummy, connection.Kind));
                previous = dummy;
            }

            segments.Add(new LayoutSegment(previous, head, connection.Kind));
        }

        foreach (var depth in layerOrder.Keys.ToArray())
        {
            layerOrder[depth] = layerOrder[depth]
                .OrderBy(vertex => vertex.InitialRow)
                .ThenBy(vertex => vertex.StableIndex)
                .ToList();
        }

        return new SegmentedLayoutGraph(layerOrder, segments, realVertices);
    }

    private static Dictionary<LayoutVertex, double> AssignRowsPreservingLayerOrder(
        IReadOnlyDictionary<int, List<LayoutVertex>> layerOrder)
    {
        var rows = new Dictionary<LayoutVertex, double>();

        foreach (var orderedVertices in layerOrder.Values)
        {
            var rowSlots = orderedVertices
                .Select(vertex => vertex.InitialRow)
                .OrderBy(row => row)
                .ToArray();

            for (var index = 0; index < orderedVertices.Count; index++)
            {
                rows[orderedVertices[index]] = rowSlots[index];
            }
        }

        return rows;
    }

    private static Dictionary<LayoutVertex, double> MinimizeSegmentLengths(
        SegmentedLayoutGraph layout,
        IReadOnlyDictionary<LayoutVertex, double> initialRows)
    {
        var rows = initialRows.ToDictionary(pair => pair.Key, pair => pair.Value);
        var neighborsByVertex = layout.LayerOrder
            .SelectMany(pair => pair.Value)
            .ToDictionary(vertex => vertex, _ => new List<WeightedLayoutNeighbor>());

        foreach (var segment in layout.Segments)
        {
            if (!neighborsByVertex.ContainsKey(segment.Tail) ||
                !neighborsByVertex.ContainsKey(segment.Head))
            {
                continue;
            }

            var weight = GetConnectionWeight(segment.Kind);
            neighborsByVertex[segment.Tail].Add(new WeightedLayoutNeighbor(segment.Head, weight));
            neighborsByVertex[segment.Head].Add(new WeightedLayoutNeighbor(segment.Tail, weight));
        }

        if (layout.Segments.Count == 0 || layout.LayerOrder.Count < 2)
        {
            NormalizeRows(rows);
            return rows;
        }

        var minDepth = layout.LayerOrder.Keys.Min();
        var maxDepth = layout.LayerOrder.Keys.Max();

        for (var pass = 0; pass < EdgeLengthMinimizationPasses; pass++)
        {
            for (var depth = minDepth; depth <= maxDepth; depth++)
            {
                AdjustLayer(depth);
            }

            for (var depth = maxDepth; depth >= minDepth; depth--)
            {
                AdjustLayer(depth);
            }

            NormalizeRows(rows);
        }

        return rows;

        void AdjustLayer(int depth)
        {
            if (!layout.LayerOrder.TryGetValue(depth, out var layer) || layer.Count == 0)
            {
                return;
            }

            var desiredRows = new double[layer.Count];
            var desiredWeights = new double[layer.Count];

            for (var index = 0; index < layer.Count; index++)
            {
                var vertex = layer[index];
                var weightedRowSum = rows.GetValueOrDefault(vertex) * RowInertiaWeight;
                var totalWeight = RowInertiaWeight;

                foreach (var neighbor in neighborsByVertex[vertex])
                {
                    if (!rows.TryGetValue(neighbor.Vertex, out var neighborRow))
                    {
                        continue;
                    }

                    weightedRowSum += neighborRow * neighbor.Weight;
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

    private static void ImproveLayerByAdjacentTransposes(
        Dictionary<int, List<LayoutVertex>> layerOrder,
        IReadOnlyList<LayoutSegment> segments,
        int depth)
    {
        if (!layerOrder.TryGetValue(depth, out var layer) || layer.Count <= 1)
        {
            return;
        }

        var improved = true;
        var attempts = 0;

        while (improved && attempts++ < AdjacentTransposePasses)
        {
            improved = false;
            var currentScore = CountCrossingsAroundLayer(layerOrder, segments, depth);
            for (var index = 0; index < layer.Count - 1; index++)
            {
                (layer[index], layer[index + 1]) = (layer[index + 1], layer[index]);
                var after = CountCrossingsAroundLayer(layerOrder, segments, depth);

                if (after < currentScore)
                {
                    currentScore = after;
                    improved = true;
                    continue;
                }

                (layer[index], layer[index + 1]) = (layer[index + 1], layer[index]);
            }
        }
    }

    private static double CountCrossingsAroundLayer(
        IReadOnlyDictionary<int, List<LayoutVertex>> layerOrder,
        IReadOnlyList<LayoutSegment> segments,
        int depth)
    {
        return CountAdjacentLayerCrossings(layerOrder, segments, depth - 1) +
               CountAdjacentLayerCrossings(layerOrder, segments, depth);
    }

    private static double CountAdjacentLayerCrossings(
        IReadOnlyDictionary<int, List<LayoutVertex>> layerOrder,
        IReadOnlyList<LayoutSegment> segments,
        int leftDepth)
    {
        if (!layerOrder.ContainsKey(leftDepth) ||
            !layerOrder.ContainsKey(leftDepth + 1))
        {
            return 0;
        }

        var positions = layerOrder
            .SelectMany(pair => pair.Value.Select((vertex, index) => new { vertex, index }))
            .ToDictionary(item => item.vertex, item => item.index);
        var adjacentSegments = segments
            .Where(segment => segment.Tail.Layer == leftDepth &&
                              segment.Head.Layer == leftDepth + 1)
            .ToArray();
        var crossings = 0d;

        for (var i = 0; i < adjacentSegments.Length; i++)
        {
            for (var j = i + 1; j < adjacentSegments.Length; j++)
            {
                var first = adjacentSegments[i];
                var second = adjacentSegments[j];
                var tailOrder = positions[first.Tail].CompareTo(positions[second.Tail]);
                var headOrder = positions[first.Head].CompareTo(positions[second.Head]);

                if (tailOrder != 0 && headOrder != 0 && tailOrder != headOrder)
                {
                    crossings += GetConnectionWeight(first.Kind) * GetConnectionWeight(second.Kind);
                }
            }
        }

        return crossings;
    }

    private static double Lerp(double from, double to, double ratio)
    {
        return from + (to - from) * ratio;
    }

    private static double GetConnectionWeight(RoadmapConnectionKind kind)
    {
        return kind == RoadmapConnectionKind.Blocks ? BlocksEdgeWeight : ContainsEdgeWeight;
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

    private static void NormalizeRows(Dictionary<TaskItemViewModel, double> rows)
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

        foreach (var task in rows.Keys.ToArray())
        {
            rows[task] -= minRow;
        }
    }

    private static void NormalizeRows(Dictionary<LayoutVertex, double> rows)
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

        foreach (var vertex in rows.Keys.ToArray())
        {
            rows[vertex] -= minRow;
        }
    }

    private static void ApplyLayout(
        IReadOnlyList<RoadmapNode> nodes,
        IReadOnlyDictionary<TaskItemViewModel, int> depths,
        IReadOnlyDictionary<TaskItemViewModel, double> rows)
    {
        foreach (var node in nodes)
        {
            node.Location = new Point(
                StartX + depths.GetValueOrDefault(node.TaskItem) * HorizontalSpacing,
                StartY + rows.GetValueOrDefault(node.TaskItem) * VerticalSpacing);
        }
    }

    private readonly record struct ConnectionDefinition(
        TaskItemViewModel Tail,
        TaskItemViewModel Head,
        RoadmapConnectionKind Kind);

    private readonly record struct RowBlock(
        int Start,
        int End,
        double Weight,
        double Value);

    private sealed class LayoutVertex
    {
        public LayoutVertex(
            TaskItemViewModel? task,
            int layer,
            double initialRow,
            int stableIndex)
        {
            Task = task;
            Layer = layer;
            InitialRow = initialRow;
            StableIndex = stableIndex;
        }

        public TaskItemViewModel? Task { get; }

        public int Layer { get; }

        public double InitialRow { get; }

        public int StableIndex { get; }
    }

    private sealed class SegmentedLayoutGraph
    {
        public SegmentedLayoutGraph(
            Dictionary<int, List<LayoutVertex>> layerOrder,
            IReadOnlyList<LayoutSegment> segments,
            IReadOnlyDictionary<TaskItemViewModel, LayoutVertex> realVertices)
        {
            LayerOrder = layerOrder;
            Segments = segments;
            RealVertices = realVertices;
        }

        public Dictionary<int, List<LayoutVertex>> LayerOrder { get; }

        public IReadOnlyList<LayoutSegment> Segments { get; }

        public IReadOnlyDictionary<TaskItemViewModel, LayoutVertex> RealVertices { get; }
    }

    private readonly record struct LayoutSegment(
        LayoutVertex Tail,
        LayoutVertex Head,
        RoadmapConnectionKind Kind);

    private readonly record struct WeightedLayoutNeighbor(
        LayoutVertex Vertex,
        double Weight);
}
