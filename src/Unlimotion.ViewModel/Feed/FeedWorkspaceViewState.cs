using System.Collections.Generic;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.ViewModel.Feed;

public sealed record FeedWorkspaceViewState(
    WorkspaceLocation Location,
    IReadOnlyDictionary<string, bool> CollapsedDays);
