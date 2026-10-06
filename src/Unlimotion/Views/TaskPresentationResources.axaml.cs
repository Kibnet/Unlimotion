using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Unlimotion.Views;

/// <summary>Shared templates and theme styles; this resource host has no screen content.</summary>
public partial class TaskPresentationResources : UserControl
{
    public TaskPresentationResources() => InitializeComponent();

    private static TaskPresentationControl? Owner(object? sender)
    {
        if (sender is not Control control) return null;
        var owner = control.GetVisualAncestors().OfType<TaskPresentationControl>().FirstOrDefault()
            ?? control.GetLogicalAncestors().OfType<TaskPresentationControl>().FirstOrDefault();
        if (owner is not null) return owner;
        var menu = control.GetLogicalAncestors().OfType<ContextMenu>().FirstOrDefault();
        return menu?.PlacementTarget?.GetVisualAncestors().OfType<TaskPresentationControl>().FirstOrDefault();
    }

    private void TaskTreeContextMenuItem_OnClick(object? sender, RoutedEventArgs e) =>
        Owner(sender)?.TaskTreeContextMenuItem_OnClick(sender, e);
    private void CompletionCriterionRemoveButton_OnClick(object? sender, RoutedEventArgs e) =>
        Owner(sender)?.CompletionCriterionRemoveButton_OnClick(sender, e);
    private void InlineTaskTitleText_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
        Owner(sender)?.InlineTaskTitleText_OnPointerPressed(sender, e);
    private void TaskOpenActions_OnClick(object? sender, RoutedEventArgs e) =>
        Owner(sender)?.TaskOpenActions_OnClick(sender, e);
}
