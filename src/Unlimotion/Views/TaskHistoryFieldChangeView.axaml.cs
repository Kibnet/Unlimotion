using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion.Views;

/// <summary>A comparison keeps its values aligned; full text lives only in the expanded row.</summary>
public partial class TaskHistoryFieldChangeView : UserControl
{
    private MainControl? _owner;
    public bool IsDetailsExpanded => DetailsPanel.IsVisible;

    public TaskHistoryFieldChangeView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateComparisonLayout();
        DataContextChanged += (_, _) =>
        {
            _owner?.CloseTaskHistoryDetails(this);
            CollapseDetails();
            Dispatcher.UIThread.Post(UpdateComparisonLayout);
        };
        AttachedToVisualTree += (_, _) =>
        {
            _owner = this.FindAncestorOfType<MainControl>();
            UpdateComparisonLayout();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _owner?.CloseTaskHistoryDetails(this);
            CollapseDetails();
            _owner = null;
        };
    }

    internal void ExpandDetails(string oldValue, string newValue)
    {
        var restoreFocus = ShowDetailsButton.IsFocused;
        FullOldValue.Text = DataContext is TaskHistoryFieldChange oldChange
            ? PresentValue(oldChange, oldValue, oldSide: true, preview: false) : oldValue;
        FullNewValue.Text = DataContext is TaskHistoryFieldChange newChange
            ? PresentValue(newChange, newValue, oldSide: false, preview: false) : newValue;
        FullOldViewer.Offset = default;
        FullNewViewer.Offset = default;
        DetailsPanel.IsVisible = true;
        UpdateComparisonLayout();
        if (restoreFocus)
            Dispatcher.UIThread.Post(() =>
            {
                if (IsDetailsExpanded && TopLevel.GetTopLevel(this) is not null)
                    CollapseDetailsButton.Focus();
            }, DispatcherPriority.Loaded);
    }

    internal void CollapseDetails()
    {
        DetailsPanel.IsVisible = false;
        FullOldValue.Text = null;
        FullNewValue.Text = null;
        UpdateComparisonLayout();
    }

    private void UpdateComparisonLayout()
    {
        if (DataContext is not TaskHistoryFieldChange change)
            return;

        var name = change.ChangeType switch
        {
            TaskHistoryChangeType.Added => $"+ {change.DisplayName}",
            TaskHistoryChangeType.Removed => $"− {change.DisplayName}",
            _ => change.DisplayName
        };
        InlineFieldName.Text = name + ":";
        ComparisonFieldName.Text = name;
        DetailsFieldName.Text = name;
        var oldValue = PresentValue(change, change.OldValueDisplay, oldSide: true, preview: true);
        var newValue = PresentValue(change, change.NewValueDisplay, oldSide: false, preview: true);
        InlineOldValue.Text = PreviewOld.Text = oldValue;
        InlineNewValue.Text = PreviewNew.Text = newValue;
        InlineOldValue.IsVisible = !change.HasSingleValue || change.ChangeType == TaskHistoryChangeType.Removed;
        InlineNewValue.IsVisible = !change.HasSingleValue || change.ChangeType == TaskHistoryChangeType.Added;
        InlineArrow.IsVisible = !change.HasSingleValue;
        var taskTitle = RelatedTaskTitle(change);
        var hasDetails = change.HasDetails || (taskTitle?.Length ?? 0) > 40;
        ToolTip.SetTip(this, string.IsNullOrWhiteSpace(taskTitle) ? null :
            Localization.Format("TaskHistoryReferencedTaskTip", taskTitle, change.ReferencedTaskId));
        var multiline = oldValue.Contains('\n') || newValue.Contains('\n');
        double TextWidth(string text, FontWeight weight)
        {
            using var layout = new TextLayout(text, new Typeface(FontFamily, FontStyle, weight), FontSize, Foreground);
            return layout.WidthIncludingTrailingWhitespace;
        }
        var inlineWidth = TextWidth(name + ":", FontWeight.SemiBold) + TextWidth(oldValue, FontWeight) +
                          TextWidth(newValue, FontWeight) +
                          (change.HasSingleValue ? 5 : TextWidth("→", FontWeight) + 15);
        var inline = !hasDetails && !multiline && Bounds.Width > 0 && inlineWidth <= Bounds.Width;
        InlineRow.IsVisible = !IsDetailsExpanded && inline;
        ComparisonRow.IsVisible = !IsDetailsExpanded && !inline;
        ShowDetailsButton.IsVisible = hasDetails || multiline || oldValue.Length > 40 || newValue.Length > 40;
        ArrangeValues(PreviewGrid, PreviewOldLabel, PreviewOld, PreviewNewLabel, PreviewNew);
        ArrangeValues(FullValueGrid, FullOldLabel, FullOldViewer, FullNewLabel, FullNewViewer);
    }

    private void ArrangeValues(Grid grid, Control oldLabel, Control oldValue, Control newLabel, Control newValue)
    {
        if (DataContext is TaskHistoryFieldChange { HasSingleValue: true } change)
        {
            oldLabel.IsVisible = newLabel.IsVisible = false;
            oldValue.IsVisible = change.ChangeType == TaskHistoryChangeType.Removed;
            newValue.IsVisible = change.ChangeType == TaskHistoryChangeType.Added;
            if (grid.ColumnDefinitions.Count != 1)
                grid.ColumnDefinitions = new ColumnDefinitions("*");
            Grid.SetRow(oldValue, 0);
            Grid.SetColumn(oldValue, 0);
            Grid.SetRow(newValue, 0);
            Grid.SetColumn(newValue, 0);
            return;
        }

        oldLabel.IsVisible = newLabel.IsVisible = oldValue.IsVisible = newValue.IsVisible = true;
        var wide = Bounds.Width >= 440;
        // Avoid invalidating layout again when only the row height changed.
        if (grid.ColumnDefinitions.Count == 2 &&
            (grid.ColumnDefinitions[0].Width.GridUnitType == GridUnitType.Star) == wide)
            return;
        grid.ColumnDefinitions = new ColumnDefinitions(wide ? "*,*" : "Auto,*");
        Grid.SetRow(oldLabel, 0);
        Grid.SetColumn(oldLabel, 0);
        Grid.SetRow(oldValue, wide ? 1 : 0);
        Grid.SetColumn(oldValue, wide ? 0 : 1);
        Grid.SetRow(newLabel, wide ? 0 : 1);
        Grid.SetColumn(newLabel, wide ? 1 : 0);
        Grid.SetRow(newValue, 1);
        Grid.SetColumn(newValue, 1);
    }

    private string? RelatedTaskTitle(TaskHistoryFieldChange change) =>
        change.HasSingleValue && change.ReferencedTaskId is { } id
            ? _owner?.ResolveTaskHistoryTaskTitle(id) : null;

    private string PresentValue(TaskHistoryFieldChange change, string value, bool oldSide, bool preview)
    {
        if (oldSide != (change.ChangeType == TaskHistoryChangeType.Removed) ||
            RelatedTaskTitle(change) is not { } title || string.IsNullOrWhiteSpace(title))
            return value;

        if (preview)
        {
            title = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (title.Length > 40)
            {
                var elements = StringInfo.GetTextElementEnumerator(title);
                var count = 0;
                while (elements.MoveNext())
                    if (count++ == 40)
                    {
                        title = title[..elements.ElementIndex] + "…";
                        break;
                    }
            }
        }
        return $"{title} ({value})";
    }

    private async void ShowDetailsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindAncestorOfType<MainControl>() is { } main)
            await main.ToggleTaskHistoryDetailsAsync(this);
        e.Handled = true;
    }

    private void CloseDetailsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        this.FindAncestorOfType<MainControl>()?.CloseTaskHistoryDetails(this);
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsDetailsExpanded && TopLevel.GetTopLevel(this) is not null)
                ShowDetailsButton.Focus();
        }, DispatcherPriority.Loaded);
        e.Handled = true;
    }
}
