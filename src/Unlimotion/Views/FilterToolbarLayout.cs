using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using SearchBarView = Unlimotion.Views.SearchControl.SearchBar;
using SearchControlView = Unlimotion.Views.SearchControl.SearchControl;

namespace Unlimotion.Views;

internal static class FilterToolbarLayout
{
    private static readonly ConditionalWeakTable<EmojiFilterMultiSelectSearchBox, RegularEmojiSummaryWidths>
        RegularEmojiSummaryWidthsByControl = new();

    public static void ApplyAdaptiveEmojiFilterWidths(
        Grid toolbar,
        WrapPanel primaryActions,
        SearchBarView searchBar)
    {
        var emojiFilters = primaryActions.Children
            .OfType<EmojiFilterMultiSelectSearchBox>()
            .ToArray();
        if (emojiFilters.Length == 0)
        {
            return;
        }

        var regularWidths = emojiFilters
            .Select(GetRegularEmojiSummaryWidths)
            .ToArray();
        ApplyEmojiSummaryWidths(emojiFilters, regularWidths);

        primaryActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarWidth = GetPositiveLayoutSize(toolbar.Bounds.Width);
        if (toolbarWidth <= 0)
        {
            toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            toolbarWidth = GetPositiveLayoutSize(toolbar.DesiredSize.Width);
        }

        if (toolbarWidth <= 0)
        {
            return;
        }

        var regularActionsWidth = GetPositiveLayoutSize(primaryActions.DesiredSize.Width);
        var minimumSearchWidth = MeasureMinimumVisibleSearchWidth(searchBar);
        var requiredRegularWidth = regularActionsWidth +
                                   GetPositiveLayoutSize(toolbar.ColumnSpacing) +
                                   minimumSearchWidth;

        if (requiredRegularWidth <= toolbarWidth)
        {
            return;
        }

        var compactSummaryWidth = MeasureCompactEmojiSummaryWidth(primaryActions, searchBar);
        if (compactSummaryWidth <= 0)
        {
            return;
        }

        foreach (var emojiFilter in emojiFilters)
        {
            var regularWidth = GetRegularEmojiSummaryWidths(emojiFilter);
            var summaryWidth = Math.Min(regularWidth.Width, compactSummaryWidth);
            emojiFilter.SummaryWidth = summaryWidth;
            emojiFilter.SummaryMinWidth = Math.Min(regularWidth.MinWidth, summaryWidth);
        }
    }

    private static RegularEmojiSummaryWidths GetRegularEmojiSummaryWidths(
        EmojiFilterMultiSelectSearchBox emojiFilter)
    {
        return RegularEmojiSummaryWidthsByControl.GetValue(
            emojiFilter,
            static control => new RegularEmojiSummaryWidths(control.SummaryWidth, control.SummaryMinWidth));
    }

    private static void ApplyEmojiSummaryWidths(
        EmojiFilterMultiSelectSearchBox[] emojiFilters,
        RegularEmojiSummaryWidths[] regularWidths)
    {
        for (var i = 0; i < emojiFilters.Length; i++)
        {
            emojiFilters[i].SummaryWidth = regularWidths[i].Width;
            emojiFilters[i].SummaryMinWidth = regularWidths[i].MinWidth;
        }
    }

    private static double MeasureMinimumVisibleSearchWidth(SearchBarView searchBar)
    {
        searchBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var searchControl = searchBar.GetVisualDescendants()
            .OfType<SearchControlView>()
            .FirstOrDefault();
        searchControl?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var minimumWidth = Math.Max(
            GetPositiveLayoutSize(searchBar.MinWidth),
            GetPositiveLayoutSize(searchControl?.MinWidth ?? 0));
        var visibleHeight = Math.Max(
            GetPositiveLayoutSize(searchBar.Bounds.Height),
            GetPositiveLayoutSize(searchBar.DesiredSize.Height));

        return Math.Max(minimumWidth, visibleHeight);
    }

    private static double MeasureCompactEmojiSummaryWidth(WrapPanel primaryActions, SearchBarView searchBar)
    {
        var filtersButton = primaryActions.Children
            .OfType<DropDownButton>()
            .FirstOrDefault(static button => button.Classes.Contains("FilterToolbarFiltersButton"));
        if (filtersButton != null)
        {
            filtersButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var filtersButtonWidth = Math.Max(
                GetPositiveLayoutSize(filtersButton.Bounds.Width),
                GetPositiveLayoutSize(filtersButton.DesiredSize.Width));
            if (filtersButtonWidth > 0)
            {
                return filtersButtonWidth;
            }
        }

        searchBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(
            GetPositiveLayoutSize(searchBar.Bounds.Height),
            GetPositiveLayoutSize(searchBar.DesiredSize.Height));
    }

    private static double GetPositiveLayoutSize(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : 0;
    }

    private sealed record RegularEmojiSummaryWidths(double Width, double MinWidth);
}
