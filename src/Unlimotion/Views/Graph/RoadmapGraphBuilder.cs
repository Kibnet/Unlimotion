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
    private const int EdgeLengthMinimizationPasses = 6;
    private const double MinimumRowGap = 1;
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
        var layerOrder = tasks
            .GroupBy(task => depths.GetValueOrDefault(task))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(task => initialRows.GetValueOrDefault(task))
                    .ThenBy(task => firstSeen[task])
                    .ToList());

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
            }

            for (var depth = maxDepth - 1; depth >= minDepth; depth--)
            {
                ReorderLayer(depth, useIncoming: false);
            }
        }

        var result = new Dictionary<TaskItemViewModel, double>();
        foreach (var (depth, orderedTasks) in layerOrder)
        {
            var rowSlots = orderedTasks
                .Select(task => initialRows.GetValueOrDefault(task))
                .OrderBy(row => row)
                .ToArray();

            for (var index = 0; index < orderedTasks.Count; index++)
            {
                result[orderedTasks[index]] = rowSlots[index];
            }
        }

        return MinimizeEdgeLengths(tasks, connections, depths, result, firstSeen);

        void ReorderLayer(int depth, bool useIncoming)
        {
            if (!layerOrder.TryGetValue(depth, out var layer) || layer.Count <= 1)
            {
                return;
            }

            var positions = BuildCurrentPositions();
            layerOrder[depth] = layer
                .Select((task, index) => new
                {
                    Task = task,
                    CurrentIndex = index,
                    Weight = GetBarycenter(task, depth, useIncoming, positions)
                })
                .OrderBy(item => item.Weight)
                .ThenBy(item => item.CurrentIndex)
                .ThenBy(item => firstSeen[item.Task])
                .Select(item => item.Task)
                .ToList();
        }

        Dictionary<TaskItemViewModel, double> BuildCurrentPositions()
        {
            return layerOrder
                .SelectMany(pair => pair.Value.Select((task, index) => new { task, index }))
                .ToDictionary(item => item.task, item => (double)item.index);
        }

        double GetBarycenter(
            TaskItemViewModel task,
            int depth,
            bool useIncoming,
            IReadOnlyDictionary<TaskItemViewModel, double> positions)
        {
            var neighbors = connections
                .Where(connection => useIncoming
                    ? connection.Head == task && depths.GetValueOrDefault(connection.Tail) < depth
                    : connection.Tail == task && depths.GetValueOrDefault(connection.Head) > depth)
                .Select(connection => useIncoming ? connection.Tail : connection.Head)
                .Where(positions.ContainsKey)
                .Select(neighbor => positions[neighbor])
                .ToArray();

            return neighbors.Length == 0 ? positions[task] : neighbors.Average();
        }
    }

    private static Dictionary<TaskItemViewModel, double> MinimizeEdgeLengths(
        IEnumerable<TaskItemViewModel> tasks,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<TaskItemViewModel, int> depths,
        IReadOnlyDictionary<TaskItemViewModel, double> initialRows,
        IReadOnlyDictionary<TaskItemViewModel, int> firstSeen)
    {
        var nodes = tasks.ToList();
        var rows = initialRows.ToDictionary(pair => pair.Key, pair => pair.Value);
        var neighborsByTask = nodes.ToDictionary(task => task, _ => new List<WeightedNeighbor>());
        var layerOrder = nodes
            .GroupBy(task => depths.GetValueOrDefault(task))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(task => rows.GetValueOrDefault(task))
                    .ThenBy(task => firstSeen[task])
                    .ToList());

        foreach (var connection in connections)
        {
            if (!neighborsByTask.ContainsKey(connection.Tail) ||
                !neighborsByTask.ContainsKey(connection.Head))
            {
                continue;
            }

            var weight = GetConnectionWeight(connection.Kind);
            neighborsByTask[connection.Tail].Add(new WeightedNeighbor(connection.Head, weight));
            neighborsByTask[connection.Head].Add(new WeightedNeighbor(connection.Tail, weight));
        }

        if (connections.Count == 0 || layerOrder.Count < 2)
        {
            NormalizeRows(rows);
            return rows;
        }

        var minDepth = layerOrder.Keys.Min();
        var maxDepth = layerOrder.Keys.Max();

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
            if (!layerOrder.TryGetValue(depth, out var layer) || layer.Count == 0)
            {
                return;
            }

            var desiredRows = new double[layer.Count];
            var desiredWeights = new double[layer.Count];

            for (var index = 0; index < layer.Count; index++)
            {
                var task = layer[index];
                var weightedRowSum = rows.GetValueOrDefault(task) * RowInertiaWeight;
                var totalWeight = RowInertiaWeight;

                foreach (var neighbor in neighborsByTask[task])
                {
                    if (!rows.TryGetValue(neighbor.Task, out var neighborRow))
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

    private readonly record struct WeightedNeighbor(
        TaskItemViewModel Task,
        double Weight);
}
