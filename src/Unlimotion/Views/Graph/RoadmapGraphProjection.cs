using System.Collections.Generic;

namespace Unlimotion.Views.Graph;

public sealed class RoadmapGraphProjection
{
    public RoadmapGraphProjection(
        IReadOnlyList<RoadmapNode> nodes,
        IReadOnlyList<RoadmapConnection> connections)
    {
        Nodes = nodes;
        Connections = connections;
    }

    public IReadOnlyList<RoadmapNode> Nodes { get; }

    public IReadOnlyList<RoadmapConnection> Connections { get; }
}
