using System.Collections.Generic;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.ViewModel;

public sealed record TaskDocumentViewportState(double OffsetX, double OffsetY);

public sealed record RoadmapDocumentState(double X, double Y, double Zoom,
    IReadOnlyList<string> SelectedIds, bool ToolbarExpanded, bool MinimapExpanded);

public sealed record TaskListDocumentState(
    TaskListKind Kind,
    TaskListFilterSnapshot Filters,
    double OffsetX,
    double OffsetY,
    IReadOnlyList<string> SelectedIds,
    IReadOnlyList<string> ExpandedIds,
    RoadmapDocumentState? Roadmap = null);
