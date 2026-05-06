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

internal sealed class RoadmapGraphBuildInput
{
    public RoadmapGraphBuildInput(
        IReadOnlyList<RoadmapGraphNodeInput> nodes,
        IReadOnlyList<RoadmapGraphConnectionInput> connections)
    {
        Nodes = nodes;
        Connections = connections;
    }

    public IReadOnlyList<RoadmapGraphNodeInput> Nodes { get; }

    public IReadOnlyList<RoadmapGraphConnectionInput> Connections { get; }
}

internal sealed class RoadmapGraphNodeInput
{
    public RoadmapGraphNodeInput(TaskItemViewModel taskItem, string id, double width)
    {
        TaskItem = taskItem;
        Id = id;
        Width = width;
    }

    public TaskItemViewModel TaskItem { get; }

    public string Id { get; }

    public double Width { get; }
}

internal sealed class RoadmapGraphConnectionInput
{
    public RoadmapGraphConnectionInput(
        RoadmapGraphNodeInput tail,
        RoadmapGraphNodeInput head,
        RoadmapConnectionKind kind)
    {
        Tail = tail;
        Head = head;
        Kind = kind;
    }

    public RoadmapGraphNodeInput Tail { get; }

    public RoadmapGraphNodeInput Head { get; }

    public RoadmapConnectionKind Kind { get; }
}

public static class RoadmapGraphBuilder
{
    private const double StartX = 24;
    private const double StartY = 24;
    private const double NodeSeparation = 10;
    private const double LayerSeparation = 140;
    private const double MinimumRowGap = RoadmapNode.Height + NodeSeparation;
    private const double DummyRowGap = NodeSeparation;
    private const double RowInertiaWeight = 0.2;
    private const double EstimatedTextCharacterWidth = 7.4;
    private const double NodeChromeWidth = 54;
    private const double RepeaterMarkerWidth = 24;
    private const double EmojiWidth = 24;
    private const int BlocksEdgeWeight = 20;
    private const int ContainsEdgeWeight = 5;
    private const int LayerOrderingPasses = 24;
    private const int AdjacentSwapPasses = 4;
    private const int LengthAwareSwapPasses = 4;
    private const int TreeBalancingPasses = 8;
    private const int RowRelaxationPasses = 16;
    private const int AlternativePathCacheMinOutgoing = 5;
    private const double EdgeLengthSwapTolerance = 0.5;

    public static RoadmapGraphProjection Build(
        ReadOnlyObservableCollection<TaskWrapperViewModel> roots,
        IReadOnlyDictionary<string, double>? measuredNodeWidths = null)
    {
        return Build(Capture(roots, measuredNodeWidths));
    }

    internal static RoadmapGraphBuildInput Capture(
        ReadOnlyObservableCollection<TaskWrapperViewModel> roots,
        IReadOnlyDictionary<string, double>? measuredNodeWidths = null)
    {
        var nodesByTask = new Dictionary<TaskItemViewModel, RoadmapGraphNodeInput>();
        var processed = new HashSet<TaskItemViewModel>();
        var queue = new Queue<TaskWrapperViewModel>();
        var connections = new List<RoadmapGraphConnectionInput>();
        var connectionKeys = new HashSet<string>();

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

            var subTasks = task.SubTasks.ToArray();
            var containsTaskIds = subTasks.Select(e => e.TaskItem.Id).ToArray();
            var blockedByIds = taskItem.BlockedBy.ToHashSet(StringComparer.Ordinal);

            foreach (var containsTask in subTasks)
            {
                var child = containsTask.TaskItem;
                EnsureNode(child);

                var childBlocks = child.Blocks.ToArray();
                var childBlocksAnotherChild = childBlocks.Any(item =>
                    containsTaskIds.Where(id => id != child.Id).Contains(item));
                var hasChildBlocksBlocker = childBlocks.Any(blockedByIds.Contains);

                if (!hasChildBlocksBlocker && !childBlocksAnotherChild)
                {
                    AddConnection(child, taskItem, RoadmapConnectionKind.Contains);
                }

                if (!processed.Contains(child))
                {
                    queue.Enqueue(containsTask);
                }
            }

            foreach (var blockedTask in taskItem.BlocksTasks.ToArray())
            {
                EnsureNode(blockedTask);
                AddConnection(taskItem, blockedTask, RoadmapConnectionKind.Blocks);
            }
        }

        return new RoadmapGraphBuildInput(nodesByTask.Values.ToList(), connections);

        RoadmapGraphNodeInput EnsureNode(TaskItemViewModel taskItem)
        {
            if (nodesByTask.TryGetValue(taskItem, out var node))
            {
                return node;
            }

            node = new RoadmapGraphNodeInput(
                taskItem,
                taskItem.Id,
                ResolveNodeWidth(taskItem));
            nodesByTask.Add(taskItem, node);
            return node;
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
                connections.Add(new RoadmapGraphConnectionInput(
                    EnsureNode(tail),
                    EnsureNode(head),
                    kind));
            }
        }
    }

    internal static RoadmapGraphProjection Build(RoadmapGraphBuildInput input)
    {
        var firstSeen = new Dictionary<RoadmapNode, int>();
        var nodesByInput = new Dictionary<RoadmapGraphNodeInput, RoadmapNode>();
        var nextSeenIndex = 0;

        foreach (var inputNode in input.Nodes)
        {
            var node = new RoadmapNode(inputNode.TaskItem);
            node.SetMeasuredWidth(inputNode.Width);
            nodesByInput.Add(inputNode, node);
            firstSeen.Add(node, nextSeenIndex++);
        }

        var connections = input.Connections
            .Where(connection =>
                nodesByInput.ContainsKey(connection.Tail) &&
                nodesByInput.ContainsKey(connection.Head))
            .Select(connection => new ConnectionDefinition(
                nodesByInput[connection.Tail],
                nodesByInput[connection.Head],
                connection.Kind))
            .ToList();

        var visibleConnections = RemoveRedundantConnections(connections);
        var nodes = ApplySugiyamaLayout(nodesByInput.Values, visibleConnections, firstSeen);

        return new RoadmapGraphProjection(
            nodes,
            visibleConnections
                .Select(connection => new RoadmapConnection(
                    connection.Tail,
                    connection.Head,
                    connection.Kind))
                .ToList());
    }

    private static IReadOnlyList<ConnectionDefinition> RemoveRedundantConnections(
        IReadOnlyList<ConnectionDefinition> connections)
    {
        var outgoing = connections
            .GroupBy(connection => connection.Tail)
            .ToDictionary(group => group.Key, group => group.ToList());
        var alternativeHeadsByTail = new Dictionary<RoadmapNode, HashSet<RoadmapNode>>();

        return connections
            .Where(connection => !HasAlternativePath(connection))
            .ToList();

        bool HasAlternativePath(ConnectionDefinition candidate)
        {
            if (!outgoing.TryGetValue(candidate.Tail, out var firstEdges))
            {
                return false;
            }

            if (firstEdges.Count < AlternativePathCacheMinOutgoing)
            {
                return HasAlternativePathWithEarlyExit(candidate, firstEdges);
            }

            return GetAlternativeHeads(candidate.Tail, firstEdges).Contains(candidate.Head);
        }

        bool HasAlternativePathWithEarlyExit(
            ConnectionDefinition candidate,
            IReadOnlyList<ConnectionDefinition> firstEdges)
        {
            var visited = new HashSet<RoadmapNode> { candidate.Tail };
            var queue = new Queue<RoadmapNode>();

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

        HashSet<RoadmapNode> GetAlternativeHeads(
            RoadmapNode tail,
            IReadOnlyList<ConnectionDefinition> firstEdges)
        {
            if (alternativeHeadsByTail.TryGetValue(tail, out var cached))
            {
                return cached;
            }

            var alternativeHeads = new HashSet<RoadmapNode>();
            alternativeHeadsByTail[tail] = alternativeHeads;

            foreach (var firstHead in firstEdges.Select(edge => edge.Head).Distinct())
            {
                AddAlternativeHeadsReachableThrough(tail, firstHead, alternativeHeads);
            }

            return alternativeHeads;
        }

        void AddAlternativeHeadsReachableThrough(
            RoadmapNode tail,
            RoadmapNode firstHead,
            HashSet<RoadmapNode> alternativeHeads)
        {
            var visited = new HashSet<RoadmapNode> { tail, firstHead };
            var queue = new Queue<RoadmapNode>();
            queue.Enqueue(firstHead);

            while (queue.TryDequeue(out var task))
            {
                if (!outgoing.TryGetValue(task, out var edges))
                {
                    continue;
                }

                foreach (var edge in edges)
                {
                    if (edge.Head != firstHead)
                    {
                        alternativeHeads.Add(edge.Head);
                    }

                    if (visited.Add(edge.Head))
                    {
                        queue.Enqueue(edge.Head);
                    }
                }
            }
        }
    }

    private static List<RoadmapNode> ApplySugiyamaLayout(
        IEnumerable<RoadmapNode> sourceNodes,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<RoadmapNode, int> firstSeen)
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
            node => node,
            node => "n" + firstSeen[node].ToString(CultureInfo.InvariantCulture));
        var drawingNodesByNode = new Dictionary<RoadmapNode, MsaglDrawing.Node>();

        foreach (var node in nodes.OrderBy(node => firstSeen[node]))
        {
            drawingNodesByNode[node] = graph.AddNode(nodeIds[node]);
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
                var drawingNode = drawingNodesByNode[node];
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

            ApplyMsaglLocations(nodes, drawingNodesByNode, graph.GeometryGraph.BoundingBox);
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
            .ThenBy(node => firstSeen[node])
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
        IReadOnlyDictionary<RoadmapNode, int> firstSeen)
    {
        var layers = BuildFallbackLayers(nodes, connections, firstSeen);
        var layoutGraph = BuildLayoutGraph(nodes, connections, layers, firstSeen);
        OptimizeLayerOrder(layoutGraph.LayerOrder, layoutGraph.Edges);
        var rows = BuildInitialRows(layoutGraph.LayerOrder);
        var visibleEdges = BuildVisibleLayoutEdges(connections, layoutGraph.VertexByNode);

        RelaxRows(layoutGraph.LayerOrder, layoutGraph.Edges, rows);
        rows = BalanceLayerOrderByNeighborRows(layoutGraph.LayerOrder, layoutGraph.Edges, rows);
        if (visibleEdges.Count > 0)
        {
            var rowBalancingEdges = layoutGraph.Edges.Concat(visibleEdges).ToArray();
            rows = BalanceLayerOrderByNeighborRows(layoutGraph.LayerOrder, rowBalancingEdges, rows);
            RelaxRows(layoutGraph.LayerOrder, visibleEdges, rows);
        }

        NormalizeRows(rows);

        var layerOrder = layoutGraph.LayerOrder.ToDictionary(
            group => group.Key,
            group => group.Value
                .Where(vertex => vertex.Node != null)
                .Select(vertex => vertex.Node!)
                .ToList());
        var layerX = BuildLayerPositions(layerOrder);
        foreach (var node in nodes)
        {
            var vertex = layoutGraph.VertexByNode[node];
            node.SetConnectionWidth(RoadmapNode.MaxWidth);
            node.Location = new Avalonia.Point(
                layerX[layers.GetValueOrDefault(node)],
                StartY + rows[vertex]);
        }
    }

    private static LayoutGraph BuildLayoutGraph(
        IReadOnlyList<RoadmapNode> nodes,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<RoadmapNode, int> layers,
        IReadOnlyDictionary<RoadmapNode, int> firstSeen)
    {
        var nextStableIndex = 0;
        var vertexByNode = new Dictionary<RoadmapNode, LayoutVertex>();
        var layerOrder = new Dictionary<int, List<LayoutVertex>>();

        foreach (var node in nodes.OrderBy(node => firstSeen[node]))
        {
            var layer = layers.GetValueOrDefault(node);
            var vertex = new LayoutVertex(
                node,
                layer,
                node.Location.Y,
                nextStableIndex++);
            vertexByNode[node] = vertex;

            if (!layerOrder.TryGetValue(layer, out var layerVertices))
            {
                layerVertices = new List<LayoutVertex>();
                layerOrder[layer] = layerVertices;
            }

            layerVertices.Add(vertex);
        }

        foreach (var layer in layerOrder.Values)
        {
            layer.Sort((left, right) =>
            {
                var locationComparison = left.InitialY.CompareTo(right.InitialY);
                return locationComparison != 0
                    ? locationComparison
                    : left.StableIndex.CompareTo(right.StableIndex);
            });
        }

        var layoutEdges = new List<LayoutEdge>();
        foreach (var connection in connections)
        {
            if (!vertexByNode.TryGetValue(connection.Tail, out var tail) ||
                !vertexByNode.TryGetValue(connection.Head, out var head) ||
                tail.Layer >= head.Layer)
            {
                continue;
            }

            var previous = tail;
            for (var layer = tail.Layer + 1; layer < head.Layer; layer++)
            {
                var progress = (double)(layer - tail.Layer) / (head.Layer - tail.Layer);
                var dummy = new LayoutVertex(
                    null,
                    layer,
                    tail.InitialY + (head.InitialY - tail.InitialY) * progress,
                    nextStableIndex++);

                if (!layerOrder.TryGetValue(layer, out var layerVertices))
                {
                    layerVertices = new List<LayoutVertex>();
                    layerOrder[layer] = layerVertices;
                }

                layerVertices.Add(dummy);
                layoutEdges.Add(new LayoutEdge(previous, dummy, connection.Kind));
                previous = dummy;
            }

            layoutEdges.Add(new LayoutEdge(previous, head, connection.Kind));
        }

        foreach (var layer in layerOrder.Values)
        {
            layer.Sort((left, right) =>
            {
                var locationComparison = left.InitialY.CompareTo(right.InitialY);
                return locationComparison != 0
                    ? locationComparison
                    : left.StableIndex.CompareTo(right.StableIndex);
            });
        }

        return new LayoutGraph(layerOrder, vertexByNode, layoutEdges);
    }

    private static List<LayoutEdge> BuildVisibleLayoutEdges(
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<RoadmapNode, LayoutVertex> vertexByNode)
    {
        var visibleEdges = new List<LayoutEdge>();

        foreach (var connection in connections)
        {
            if (!vertexByNode.TryGetValue(connection.Tail, out var tail) ||
                !vertexByNode.TryGetValue(connection.Head, out var head) ||
                tail.Layer >= head.Layer)
            {
                continue;
            }

            visibleEdges.Add(new LayoutEdge(tail, head, connection.Kind));
        }

        return visibleEdges;
    }

    private static Dictionary<LayoutVertex, double> BalanceLayerOrderByNeighborRows(
        Dictionary<int, List<LayoutVertex>> layerOrder,
        IReadOnlyList<LayoutEdge> connections,
        Dictionary<LayoutVertex, double> rows)
    {
        if (layerOrder.Count < 2 || connections.Count == 0)
        {
            return rows;
        }

        var vertices = layerOrder.SelectMany(group => group.Value).ToList();
        var layersByVertex = layerOrder
            .SelectMany(group => group.Value.Select(vertex => new { Vertex = vertex, Layer = group.Key }))
            .ToDictionary(item => item.Vertex, item => item.Layer);
        var layoutConnections = connections
            .Where(connection =>
                connection.Tail != connection.Head &&
                layersByVertex.ContainsKey(connection.Tail) &&
                layersByVertex.ContainsKey(connection.Head) &&
                layersByVertex[connection.Tail] != layersByVertex[connection.Head])
            .ToList();

        if (layoutConnections.Count == 0)
        {
            return rows;
        }

        var connectionsByTask = layoutConnections
            .SelectMany(connection => new[]
            {
                new { Vertex = connection.Tail, Connection = connection },
                new { Vertex = connection.Head, Connection = connection }
            })
            .GroupBy(item => item.Vertex)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Connection).Distinct().ToList());
        var orderedLayers = layerOrder.Keys.OrderBy(layer => layer).ToArray();
        var orderByVertex = BuildOrderByVertex(layerOrder);
        var currentCrossings = CountWeightedCrossings(layoutConnections, orderByVertex);

        for (var pass = 0; pass < TreeBalancingPasses; pass++)
        {
            var changed = false;
            changed |= BalanceSweep(orderedLayers.Skip(1), preferLowerLayers: true);
            changed |= BalanceSweep(orderedLayers.Reverse().Skip(1), preferLowerLayers: false);
            changed |= ImproveLayerOrderByEdgeLength(layerOrder, layoutConnections, rows);

            if (!changed)
            {
                break;
            }

            rows = BuildInitialRows(layerOrder);
            RelaxRows(
                layerOrder,
                layoutConnections,
                rows);
        }

        return rows;

        bool BalanceSweep(
            IEnumerable<int> layerIndexes,
            bool preferLowerLayers)
        {
            var changed = false;

            foreach (var layerIndex in layerIndexes)
            {
                if (!layerOrder.TryGetValue(layerIndex, out var layer) || layer.Count < 2)
                {
                    continue;
                }

                var candidate = layer
                    .OrderBy(GetLayerConnectivityRank)
                    .ThenBy(node => GetDesiredNeighborRow(node, preferLowerLayers))
                    .ThenBy(node => rows[node])
                    .ThenBy(node => orderByVertex[node])
                    .ThenBy(node => node.StableIndex)
                    .ToList();

                if (HasSameOrder(layer, candidate))
                {
                    continue;
                }

                var original = layer.ToArray();
                layer.Clear();
                layer.AddRange(candidate);

                var candidateOrderByVertex = BuildOrderByVertex(layerOrder);
                var candidateCrossings = CountWeightedCrossings(layoutConnections, candidateOrderByVertex);
                if (candidateCrossings <= currentCrossings)
                {
                    currentCrossings = candidateCrossings;
                    orderByVertex = candidateOrderByVertex;
                    changed = true;
                    continue;
                }

                layer.Clear();
                layer.AddRange(original);
            }

            return changed;
        }

        double GetDesiredNeighborRow(
            LayoutVertex node,
            bool preferLowerLayers)
        {
            if (!connectionsByTask.TryGetValue(node, out var nodeConnections))
            {
                return rows[node];
            }

            var weightedRow = default(WeightedRow);
            var nodeLayer = layersByVertex[node];
            foreach (var connection in nodeConnections)
            {
                var neighbor = connection.Tail == node
                    ? connection.Head
                    : connection.Tail;
                var neighborLayer = layersByVertex[neighbor];

                if (preferLowerLayers != neighborLayer < nodeLayer)
                {
                    continue;
                }

                var weight = GetConnectionWeight(connection.Kind);
                weightedRow = new WeightedRow(
                    weightedRow.WeightedSum + rows[neighbor] * weight,
                    weightedRow.Weight + weight);
            }

            return weightedRow.Weight > 0 ? weightedRow.Value : rows[node];
        }

        int GetLayerConnectivityRank(LayoutVertex node)
        {
            return connectionsByTask.ContainsKey(node) ? 0 : 1;
        }
    }

    private static void OptimizeLayerOrder(
        Dictionary<int, List<LayoutVertex>> layerOrder,
        IReadOnlyList<LayoutEdge> connections)
    {
        if (layerOrder.Count < 2 || connections.Count == 0)
        {
            return;
        }

        var vertices = layerOrder
            .SelectMany(group => group.Value)
            .ToHashSet();
        var layersByVertex = layerOrder
            .SelectMany(group => group.Value.Select(vertex => new { Vertex = vertex, Layer = group.Key }))
            .ToDictionary(item => item.Vertex, item => item.Layer);
        var layoutConnections = connections
            .Where(connection =>
                connection.Tail != connection.Head &&
                vertices.Contains(connection.Tail) &&
                vertices.Contains(connection.Head) &&
                layersByVertex[connection.Tail] != layersByVertex[connection.Head])
            .ToList();

        if (layoutConnections.Count == 0)
        {
            return;
        }

        var orderedLayers = layerOrder.Keys.OrderBy(layer => layer).ToArray();
        var orderByVertex = BuildOrderByVertex(layerOrder);

        for (var pass = 0; pass < LayerOrderingPasses; pass++)
        {
            foreach (var layerIndex in orderedLayers.Skip(1))
            {
                ReorderLayerByNeighborBarycenter(
                    layerOrder[layerIndex],
                    layoutConnections,
                    layersByVertex,
                    orderByVertex,
                    preferLowerLayers: true);
                orderByVertex = BuildOrderByVertex(layerOrder);
            }

            foreach (var layerIndex in orderedLayers.Reverse().Skip(1))
            {
                ReorderLayerByNeighborBarycenter(
                    layerOrder[layerIndex],
                    layoutConnections,
                    layersByVertex,
                    orderByVertex,
                    preferLowerLayers: false);
                orderByVertex = BuildOrderByVertex(layerOrder);
            }

            ApplyAdjacentCrossingSwaps(layerOrder, layoutConnections, orderByVertex);
        }
    }

    private static Dictionary<LayoutVertex, int> BuildOrderByVertex(
        IReadOnlyDictionary<int, List<LayoutVertex>> layerOrder)
    {
        return layerOrder
            .SelectMany(group => group.Value.Select((vertex, index) => new { Vertex = vertex, Index = index }))
            .ToDictionary(item => item.Vertex, item => item.Index);
    }

    private static bool HasSameOrder(
        IReadOnlyList<LayoutVertex> first,
        IReadOnlyList<LayoutVertex> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            if (first[index] != second[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void ReorderLayerByNeighborBarycenter(
        List<LayoutVertex> layer,
        IReadOnlyList<LayoutEdge> connections,
        IReadOnlyDictionary<LayoutVertex, int> layersByVertex,
        IReadOnlyDictionary<LayoutVertex, int> orderByVertex,
        bool preferLowerLayers)
    {
        if (layer.Count < 2)
        {
            return;
        }

        var desiredOrderByVertex = new Dictionary<LayoutVertex, WeightedOrder>();
        var currentLayer = layer.ToHashSet();

        foreach (var connection in connections)
        {
            AddNeighbor(connection.Tail, connection.Head, connection.Kind);
            AddNeighbor(connection.Head, connection.Tail, connection.Kind);
        }

        layer.Sort((left, right) =>
        {
            var leftOrder = desiredOrderByVertex.TryGetValue(left, out var leftWeightedOrder)
                ? leftWeightedOrder.Value
                : orderByVertex[left];
            var rightOrder = desiredOrderByVertex.TryGetValue(right, out var rightWeightedOrder)
                ? rightWeightedOrder.Value
                : orderByVertex[right];
            var orderComparison = leftOrder.CompareTo(rightOrder);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            var currentOrderComparison = orderByVertex[left].CompareTo(orderByVertex[right]);
            return currentOrderComparison != 0
                ? currentOrderComparison
                : left.StableIndex.CompareTo(right.StableIndex);
        });

        void AddNeighbor(
            LayoutVertex vertex,
            LayoutVertex neighbor,
            RoadmapConnectionKind kind)
        {
            if (!currentLayer.Contains(vertex) ||
                !layersByVertex.TryGetValue(vertex, out var vertexLayer) ||
                !layersByVertex.TryGetValue(neighbor, out var neighborLayer))
            {
                return;
            }

            if (preferLowerLayers != neighborLayer < vertexLayer)
            {
                return;
            }

            var weight = GetConnectionWeight(kind);
            var weightedOrder = desiredOrderByVertex.GetValueOrDefault(vertex);
            desiredOrderByVertex[vertex] = new WeightedOrder(
                weightedOrder.WeightedSum + orderByVertex[neighbor] * weight,
                weightedOrder.Weight + weight);
        }
    }

    private static void ApplyAdjacentCrossingSwaps(
        IReadOnlyDictionary<int, List<LayoutVertex>> layerOrder,
        IReadOnlyList<LayoutEdge> connections,
        Dictionary<LayoutVertex, int> orderByVertex)
    {
        var connectionsByVertex = connections
            .SelectMany(connection => new[]
            {
                new { Vertex = connection.Tail, Connection = connection },
                new { Vertex = connection.Head, Connection = connection }
            })
            .GroupBy(item => item.Vertex)
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
                    var affectedConnections = GetAffectedConnections(first, second);
                    var before = CountAffectedCrossings(affectedConnections, connections, orderByVertex);

                    layer[index] = second;
                    layer[index + 1] = first;
                    orderByVertex[first] = index + 1;
                    orderByVertex[second] = index;

                    var after = CountAffectedCrossings(affectedConnections, connections, orderByVertex);
                    if (after < before)
                    {
                        improved = true;
                        continue;
                    }

                    layer[index] = first;
                    layer[index + 1] = second;
                    orderByVertex[first] = index;
                    orderByVertex[second] = index + 1;
                }
            }

            if (!improved)
            {
                return;
            }
        }

        List<LayoutEdge> GetAffectedConnections(
            LayoutVertex first,
            LayoutVertex second)
        {
            var affected = new HashSet<LayoutEdge>();
            if (connectionsByVertex.TryGetValue(first, out var firstConnections))
            {
                affected.UnionWith(firstConnections);
            }

            if (connectionsByVertex.TryGetValue(second, out var secondConnections))
            {
                affected.UnionWith(secondConnections);
            }

            return affected.ToList();
        }
    }

    private static bool ImproveLayerOrderByEdgeLength(
        IReadOnlyDictionary<int, List<LayoutVertex>> layerOrder,
        IReadOnlyList<LayoutEdge> connections,
        Dictionary<LayoutVertex, double> rows)
    {
        if (connections.Count == 0)
        {
            return false;
        }

        var connectionsByVertex = connections
            .SelectMany(connection => new[]
            {
                new { Vertex = connection.Tail, Connection = connection },
                new { Vertex = connection.Head, Connection = connection }
            })
            .GroupBy(item => item.Vertex)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Connection).Distinct().ToList());
        var orderByVertex = BuildOrderByVertex(layerOrder);
        var changed = false;

        for (var pass = 0; pass < LengthAwareSwapPasses; pass++)
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
                    var affectedConnections = GetAffectedConnections(first, second);
                    if (affectedConnections.Count == 0)
                    {
                        continue;
                    }

                    var firstRow = rows[first];
                    var secondRow = rows[second];
                    var beforeCrossings = CountAffectedCrossings(affectedConnections, connections, orderByVertex);
                    var beforeLength = CountWeightedVerticalLength(affectedConnections, rows);

                    layer[index] = second;
                    layer[index + 1] = first;
                    orderByVertex[first] = index + 1;
                    orderByVertex[second] = index;
                    rows[first] = secondRow;
                    rows[second] = firstRow;

                    var afterCrossings = CountAffectedCrossings(affectedConnections, connections, orderByVertex);
                    var afterLength = CountWeightedVerticalLength(affectedConnections, rows);
                    if (afterCrossings < beforeCrossings ||
                        afterCrossings == beforeCrossings &&
                        afterLength + EdgeLengthSwapTolerance < beforeLength)
                    {
                        improved = true;
                        changed = true;
                        continue;
                    }

                    layer[index] = first;
                    layer[index + 1] = second;
                    orderByVertex[first] = index;
                    orderByVertex[second] = index + 1;
                    rows[first] = firstRow;
                    rows[second] = secondRow;
                }
            }

            if (!improved)
            {
                break;
            }
        }

        return changed;

        List<LayoutEdge> GetAffectedConnections(
            LayoutVertex first,
            LayoutVertex second)
        {
            var affected = new HashSet<LayoutEdge>();
            if (connectionsByVertex.TryGetValue(first, out var firstConnections))
            {
                affected.UnionWith(firstConnections);
            }

            if (connectionsByVertex.TryGetValue(second, out var secondConnections))
            {
                affected.UnionWith(secondConnections);
            }

            return affected.ToList();
        }
    }

    private static double CountWeightedVerticalLength(
        IEnumerable<LayoutEdge> connections,
        IReadOnlyDictionary<LayoutVertex, double> rows)
    {
        var length = 0d;
        var visited = new HashSet<LayoutEdge>();

        foreach (var connection in connections)
        {
            if (!visited.Add(connection) ||
                !rows.TryGetValue(connection.Tail, out var tailRow) ||
                !rows.TryGetValue(connection.Head, out var headRow))
            {
                continue;
            }

            length += Math.Abs(tailRow - headRow) * GetConnectionWeight(connection.Kind);
        }

        return length;
    }

    private static long CountAffectedCrossings(
        IReadOnlyList<LayoutEdge> affectedConnections,
        IReadOnlyList<LayoutEdge> allConnections,
        IReadOnlyDictionary<LayoutVertex, int> orderByVertex)
    {
        long crossings = 0;

        foreach (var affectedConnection in affectedConnections)
        {
            foreach (var connection in allConnections)
            {
                if (affectedConnection.Equals(connection) ||
                    !HasOrderCrossing(affectedConnection, connection, orderByVertex))
                {
                    continue;
                }

                crossings += GetConnectionWeight(affectedConnection.Kind) * GetConnectionWeight(connection.Kind);
            }
        }

        return crossings;
    }

    private static long CountWeightedCrossings(
        IReadOnlyList<LayoutEdge> connections,
        IReadOnlyDictionary<LayoutVertex, int> orderByVertex)
    {
        long crossings = 0;

        for (var firstIndex = 0; firstIndex < connections.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < connections.Count; secondIndex++)
            {
                var first = connections[firstIndex];
                var second = connections[secondIndex];
                if (HasOrderCrossing(first, second, orderByVertex))
                {
                    crossings += GetConnectionWeight(first.Kind) * GetConnectionWeight(second.Kind);
                }
            }
        }

        return crossings;
    }

    private static bool HasOrderCrossing(
        LayoutEdge first,
        LayoutEdge second,
        IReadOnlyDictionary<LayoutVertex, int> orderByVertex)
    {
        if (first.Tail.Layer != second.Tail.Layer ||
            first.Head.Layer != second.Head.Layer)
        {
            return false;
        }

        var sourceOrder = orderByVertex[first.Tail].CompareTo(orderByVertex[second.Tail]);
        var targetOrder = orderByVertex[first.Head].CompareTo(orderByVertex[second.Head]);

        return sourceOrder != 0 &&
               targetOrder != 0 &&
               sourceOrder != targetOrder;
    }

    private static Dictionary<LayoutVertex, double> BuildInitialRows(
        IReadOnlyDictionary<int, List<LayoutVertex>> layerOrder)
    {
        var rows = new Dictionary<LayoutVertex, double>();

        foreach (var layer in layerOrder.Values)
        {
            var offsets = BuildRowOffsets(layer);
            for (var index = 0; index < layer.Count; index++)
            {
                rows[layer[index]] = offsets[index];
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
            x += RoadmapNode.MaxWidth + LayerSeparation;
        }

        return positions;
    }

    private static void RelaxRows(
        IReadOnlyDictionary<int, List<LayoutVertex>> layerOrder,
        IReadOnlyList<LayoutEdge> connections,
        Dictionary<LayoutVertex, double> rows)
    {
        var vertices = layerOrder.SelectMany(group => group.Value).ToHashSet();
        var neighbors = vertices.ToDictionary(vertex => vertex, _ => new List<WeightedVertexNeighbor>());

        foreach (var connection in connections)
        {
            if (!vertices.Contains(connection.Tail) ||
                !vertices.Contains(connection.Head))
            {
                continue;
            }

            var weight = GetConnectionWeight(connection.Kind);
            neighbors[connection.Tail].Add(new WeightedVertexNeighbor(connection.Head, weight));
            neighbors[connection.Head].Add(new WeightedVertexNeighbor(connection.Tail, weight));
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
            var rowOffsets = BuildRowOffsets(layer);

            for (var index = 0; index < layer.Count; index++)
            {
                var vertex = layer[index];
                var weightedRowSum = rows[vertex] * RowInertiaWeight;
                var totalWeight = RowInertiaWeight;

                foreach (var neighbor in neighbors[vertex])
                {
                    weightedRowSum += rows[neighbor.Vertex] * neighbor.Weight;
                    totalWeight += neighbor.Weight;
                }

                desiredRows[index] = weightedRowSum / totalWeight;
                desiredWeights[index] = totalWeight;
            }

            var adjustedRows = ProjectRowsPreservingOrder(
                desiredRows,
                desiredWeights,
                rowOffsets);

            for (var index = 0; index < layer.Count; index++)
            {
                rows[layer[index]] = adjustedRows[index];
            }
        }
    }

    private static double[] BuildRowOffsets(IReadOnlyList<LayoutVertex> layer)
    {
        if (layer.Count == 0)
        {
            return Array.Empty<double>();
        }

        var offsets = new double[layer.Count];
        double? previousRealOffset = layer[0].IsDummy ? null : 0;
        for (var index = 1; index < layer.Count; index++)
        {
            var current = layer[index];
            var compactGap = layer[index - 1].IsDummy || current.IsDummy
                ? DummyRowGap
                : MinimumRowGap;
            var offset = offsets[index - 1] + compactGap;

            if (!current.IsDummy)
            {
                if (previousRealOffset.HasValue)
                {
                    offset = Math.Max(offset, previousRealOffset.Value + MinimumRowGap);
                }

                previousRealOffset = offset;
            }

            offsets[index] = offset;
        }

        return offsets;
    }

    private static double[] ProjectRowsPreservingOrder(
        IReadOnlyList<double> desiredRows,
        IReadOnlyList<double> desiredWeights,
        IReadOnlyList<double> rowOffsets)
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
                desiredRows[index] - rowOffsets[index]));

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
                result[index] = block.Value + rowOffsets[index];
            }
        }

        return result;
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

        foreach (var node in rows.Keys.ToArray())
        {
            rows[node] -= minRow;
        }
    }

    private static void ApplyMsaglLocations(
        IEnumerable<RoadmapNode> nodes,
        IReadOnlyDictionary<RoadmapNode, MsaglDrawing.Node> drawingNodesByNode,
        MsaglRectangle graphBounds)
    {
        foreach (var node in nodes)
        {
            var nodeBounds = drawingNodesByNode[node].GeometryNode.BoundingBox;
            node.Location = new Avalonia.Point(
                StartX + nodeBounds.Left - graphBounds.Left,
                StartY + graphBounds.Top - nodeBounds.Top);
        }
    }

    private static void ApplyFallbackLayout(
        IReadOnlyList<RoadmapNode> nodes,
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyDictionary<RoadmapNode, int> firstSeen)
    {
        var layers = BuildFallbackLayers(nodes, connections, firstSeen);
        var rowsByLayer = nodes
            .GroupBy(node => layers.GetValueOrDefault(node))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(node => firstSeen[node])
                    .Select((node, index) => new { node, index })
                    .ToDictionary(item => item.node, item => item.index));

        foreach (var node in nodes)
        {
            var layer = layers.GetValueOrDefault(node);
            var row = rowsByLayer[layer][node];
            node.SetConnectionWidth(RoadmapNode.MaxWidth);
            node.Location = new Avalonia.Point(
                StartX + layer * (RoadmapNode.MaxWidth + LayerSeparation),
                StartY + row * (RoadmapNode.Height + NodeSeparation));
        }
    }

    private static Dictionary<RoadmapNode, int> BuildFallbackLayers(
        IEnumerable<RoadmapNode> tasks,
        IEnumerable<ConnectionDefinition> connections,
        IReadOnlyDictionary<RoadmapNode, int> firstSeen)
    {
        var nodes = tasks.ToList();
        var outgoing = nodes.ToDictionary(task => task, _ => new List<RoadmapNode>());
        var incomingCount = nodes.ToDictionary(task => task, _ => 0);
        var incoming = nodes.ToDictionary(task => task, _ => new List<RoadmapNode>());

        foreach (var connection in connections)
        {
            if (connection.Tail == connection.Head ||
                !outgoing.ContainsKey(connection.Tail) ||
                !incomingCount.ContainsKey(connection.Head) ||
                !incoming.ContainsKey(connection.Head))
            {
                continue;
            }

            outgoing[connection.Tail].Add(connection.Head);
            incoming[connection.Head].Add(connection.Tail);
            incomingCount[connection.Head]++;
        }

        return TryBuildGoalAnchoredLayers(
                   nodes,
                   outgoing,
                   incoming,
                   firstSeen)
               ?? BuildSourceAnchoredLayers(
                   nodes,
                   outgoing,
                   incomingCount,
                   firstSeen);
    }

    private static Dictionary<RoadmapNode, int>? TryBuildGoalAnchoredLayers(
        IReadOnlyList<RoadmapNode> nodes,
        IReadOnlyDictionary<RoadmapNode, List<RoadmapNode>> outgoing,
        IReadOnlyDictionary<RoadmapNode, List<RoadmapNode>> incoming,
        IReadOnlyDictionary<RoadmapNode, int> firstSeen)
    {
        var connected = nodes
            .Where(task => outgoing[task].Count > 0 || incoming[task].Count > 0)
            .ToList();
        if (connected.Count == 0)
        {
            return nodes.ToDictionary(task => task, _ => 0);
        }

        var heights = nodes.ToDictionary(task => task, _ => 0);
        var remainingOutgoing = nodes.ToDictionary(task => task, task => outgoing[task].Count);
        var queue = new Queue<RoadmapNode>(connected
            .Where(task => remainingOutgoing[task] == 0)
            .OrderBy(task => firstSeen[task]));
        var processed = new HashSet<RoadmapNode>();

        while (queue.TryDequeue(out var task))
        {
            if (!processed.Add(task))
            {
                continue;
            }

            foreach (var tail in incoming[task].OrderBy(item => firstSeen[item]))
            {
                heights[tail] = Math.Max(heights[tail], heights[task] + 1);
                remainingOutgoing[tail]--;
                if (remainingOutgoing[tail] == 0)
                {
                    queue.Enqueue(tail);
                }
            }
        }

        if (processed.Count != connected.Count)
        {
            return null;
        }

        var maxHeight = connected.Max(task => heights[task]);
        return nodes.ToDictionary(
            task => task,
            task => outgoing[task].Count == 0 && incoming[task].Count == 0
                ? 0
                : maxHeight - heights[task]);
    }

    private static Dictionary<RoadmapNode, int> BuildSourceAnchoredLayers(
        IReadOnlyList<RoadmapNode> nodes,
        IReadOnlyDictionary<RoadmapNode, List<RoadmapNode>> outgoing,
        IReadOnlyDictionary<RoadmapNode, int> incomingCount,
        IReadOnlyDictionary<RoadmapNode, int> firstSeen)
    {
        var layers = nodes.ToDictionary(task => task, _ => 0);
        var remainingIncoming = incomingCount.ToDictionary(item => item.Key, item => item.Value);
        var queue = new Queue<RoadmapNode>(nodes
            .Where(task => remainingIncoming[task] == 0)
            .OrderBy(task => firstSeen[task]));
        var processed = new HashSet<RoadmapNode>();

        while (queue.TryDequeue(out var task))
        {
            if (!processed.Add(task))
            {
                continue;
            }

            foreach (var head in outgoing[task].OrderBy(item => firstSeen[item]))
            {
                layers[head] = Math.Max(layers[head], layers[task] + 1);
                remainingIncoming[head]--;
                if (remainingIncoming[head] == 0)
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
        RoadmapNode Tail,
        RoadmapNode Head,
        RoadmapConnectionKind Kind);

    private sealed class LayoutVertex
    {
        public LayoutVertex(
            RoadmapNode? node,
            int layer,
            double initialY,
            int stableIndex)
        {
            Node = node;
            Layer = layer;
            InitialY = initialY;
            StableIndex = stableIndex;
        }

        public RoadmapNode? Node { get; }

        public int Layer { get; }

        public double InitialY { get; }

        public int StableIndex { get; }

        public bool IsDummy => Node == null;
    }

    private readonly record struct LayoutEdge(
        LayoutVertex Tail,
        LayoutVertex Head,
        RoadmapConnectionKind Kind);

    private readonly record struct LayoutGraph(
        Dictionary<int, List<LayoutVertex>> LayerOrder,
        Dictionary<RoadmapNode, LayoutVertex> VertexByNode,
        IReadOnlyList<LayoutEdge> Edges);

    private readonly record struct WeightedVertexNeighbor(
        LayoutVertex Vertex,
        int Weight);

    private readonly record struct WeightedOrder(
        double WeightedSum,
        double Weight)
    {
        public double Value => Weight <= 0 ? 0 : WeightedSum / Weight;
    }

    private readonly record struct WeightedRow(
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
