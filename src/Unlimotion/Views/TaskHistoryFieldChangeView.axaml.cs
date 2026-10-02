using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
        AttachedToVisualTree += (_, _) => _owner = this.FindAncestorOfType<MainControl>();
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
        FullOldValue.Text = oldValue;
        FullNewValue.Text = newValue;
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
        var multiline = change.OldValueDisplay.Contains('\n') || change.NewValueDisplay.Contains('\n');
        double TextWidth(string text, FontWeight weight)
        {
            using var layout = new TextLayout(text, new Typeface(FontFamily, FontStyle, weight), FontSize, Foreground);
            return layout.WidthIncludingTrailingWhitespace;
        }
        var inlineWidth = TextWidth(name + ":", FontWeight.SemiBold) + TextWidth(change.OldValueDisplay, FontWeight) +
                          TextWidth("→", FontWeight) + TextWidth(change.NewValueDisplay, FontWeight) + 15;
        var inline = !change.HasDetails && !multiline && Bounds.Width > 0 && inlineWidth <= Bounds.Width;
        InlineRow.IsVisible = !IsDetailsExpanded && inline;
        ComparisonRow.IsVisible = !IsDetailsExpanded && !inline;
        ShowDetailsButton.IsVisible = change.HasDetails || multiline ||
                                      change.OldValueDisplay.Length > 40 || change.NewValueDisplay.Length > 40;
        ArrangeValues(PreviewGrid, PreviewOldLabel, PreviewOld, PreviewNewLabel, PreviewNew);
        ArrangeValues(FullValueGrid, FullOldLabel, FullOldViewer, FullNewLabel, FullNewViewer);
    }

    private void ArrangeValues(Grid grid, Control oldLabel, Control oldValue, Control newLabel, Control newValue)
    {
        var wide = Bounds.Width >= 440;
        // Avoid invalidating layout again when only the row height changed.
        if ((grid.ColumnDefinitions[0].Width.GridUnitType == GridUnitType.Star) == wide)
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
