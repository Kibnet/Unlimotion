using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.Views;

public partial class GraphControl
{
    private void RoadmapTaskOpenActions_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TaskItemViewModel task } button ||
            ResolveMainWindowViewModel(button) is not { } owner) return;
        var menu = WorkspaceOpenMenu.Create(owner, WorkspaceLocation.ForTask(task.Id, task.Title));
        button.ContextMenu = menu;
        menu.Open(button);
        e.Handled = true;
    }

    private void RoadmapTask_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (this.FindParent<TaskListDocumentView>() is null ||
            !TryGetRoadmapTaskItem(e.Source as Control, out var task) ||
            ResolveMainWindowViewModel(e.Source as Control) is not { } owner) return;
        WorkspaceOpenMenu.Create(owner, WorkspaceLocation.ForTask(task.Id, task.Title))
            .Open(e.Source as Control ?? this);
        e.Handled = true;
    }

    public RoadmapDocumentState CaptureDocumentState() => new(
        RoadmapViewport.Location.X, RoadmapViewport.Location.Y, RoadmapViewport.Zoom,
        selectedRoadmapTaskIds.ToArray(), IsRoadmapViewportToolbarExpanded, IsRoadmapMinimapExpanded);

    public void RestoreDocumentState(RoadmapDocumentState state)
    {
        RoadmapViewport.Zoom = state.Zoom;
        RoadmapViewport.Location = new Point(state.X, state.Y);
        selectedRoadmapTaskIds.Clear();
        selectedRoadmapTaskIds.UnionWith(state.SelectedIds);
        ApplyRoadmapSelectionState();
        IsRoadmapViewportToolbarExpanded = state.ToolbarExpanded;
        IsRoadmapMinimapExpanded = state.MinimapExpanded;
    }
}
