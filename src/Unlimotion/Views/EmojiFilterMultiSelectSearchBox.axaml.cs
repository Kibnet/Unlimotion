using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Views;

public partial class EmojiFilterMultiSelectSearchBox : UserControl
{
    private const double PopupEdgeGap = 4d;
    private const double MinPopupHeight = 120d;
    private const double MaxPopupHeight = 260d;
    private const double MinPopupWidth = 280d;
    private const double MaxPopupWidth = 340d;
    public const double DefaultSummaryWidth = 112d;
    public static double DefaultSummaryMinWidth => AppearanceSettings.DefaultSearchControlHeight;
    private const double DropDownNonListHeight = 8d;
    private const double MinListHeight = 60d;
    private const double NoMatchesPanelReservedHeight = 40d;
    private const double SummaryInputPadding = 4d;
    private const string EmptySummaryToken = "🙂";
    private static readonly Thickness SummaryPadding = new(SummaryInputPadding);
    private static WeakReference<EmojiFilterMultiSelectSearchBox>? openDropDownReference;

    public static readonly StyledProperty<IEnumerable?> FiltersProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, IEnumerable?>(nameof(Filters));

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, string?>(nameof(Watermark));

    public static readonly StyledProperty<string?> NoMatchesTextProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, string?>(nameof(NoMatchesText));

    public static readonly StyledProperty<double> SummaryWidthProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, double>(
            nameof(SummaryWidth),
            DefaultSummaryWidth);

    public static readonly StyledProperty<double> SummaryMinWidthProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, double>(
            nameof(SummaryMinWidth),
            DefaultSummaryMinWidth);

    public static readonly StyledProperty<string?> SummaryAutomationIdProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, string?>(nameof(SummaryAutomationId));

    public static readonly StyledProperty<string?> SearchAutomationIdProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, string?>(nameof(SearchAutomationId));

    public static readonly StyledProperty<string?> DropDownAutomationIdProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, string?>(nameof(DropDownAutomationId));

    public static readonly StyledProperty<string?> ListAutomationIdProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, string?>(nameof(ListAutomationId));

    public static readonly StyledProperty<string?> NoMatchesAutomationIdProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, string?>(nameof(NoMatchesAutomationId));

    public static readonly StyledProperty<bool> IsExcludeProperty =
        AvaloniaProperty.Register<EmojiFilterMultiSelectSearchBox, bool>(nameof(IsExclude));

    private readonly ObservableCollection<EmojiFilter> displayedFilters = [];
    private readonly List<EmojiFilter> selectableFilters = [];
    private readonly List<INotifyPropertyChanged> subscribedFilters = [];
    private INotifyCollectionChanged? subscribedCollection;
    private bool isProgrammaticClose;
    private bool pendingSearchAfterLightDismiss;
    private bool restorePopupAfterInputDismiss;
    private bool isSearchActive;
    private bool isUpdatingInputText;
    private string searchText = string.Empty;

    public EmojiFilterMultiSelectSearchBox()
    {
        InitializeComponent();

        PART_List.ItemsSource = displayedFilters;
        PART_DropDownPopup.PlacementConstraintAdjustment =
            PopupPositionerConstraintAdjustment.SlideX |
            PopupPositionerConstraintAdjustment.SlideY |
            PopupPositionerConstraintAdjustment.FlipY |
            PopupPositionerConstraintAdjustment.ResizeY;
        // This control is hosted inside filter flyouts, whose popup roots may not expose an overlay layer.
        PART_DropDownPopup.ShouldUseOverlayLayer = false;

        PART_Input.AddHandler(PointerPressedEvent, Input_OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PART_Input.AddHandler(KeyDownEvent, Input_OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        PART_Input.AddHandler(TextInputEvent, Input_OnTextInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        PART_List.AddHandler(KeyDownEvent, List_OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        UpdateExcludeClass();
        UpdateStaticTextAndAutomation();
        RefreshFilterItems();
        UpdateInputTextFromState();
    }

    public IEnumerable? Filters
    {
        get => GetValue(FiltersProperty);
        set => SetValue(FiltersProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public string? NoMatchesText
    {
        get => GetValue(NoMatchesTextProperty);
        set => SetValue(NoMatchesTextProperty, value);
    }

    public double SummaryWidth
    {
        get => GetValue(SummaryWidthProperty);
        set => SetValue(SummaryWidthProperty, value);
    }

    public double SummaryMinWidth
    {
        get => GetValue(SummaryMinWidthProperty);
        set => SetValue(SummaryMinWidthProperty, value);
    }

    public string? SummaryAutomationId
    {
        get => GetValue(SummaryAutomationIdProperty);
        set => SetValue(SummaryAutomationIdProperty, value);
    }

    public string? SearchAutomationId
    {
        get => GetValue(SearchAutomationIdProperty);
        set => SetValue(SearchAutomationIdProperty, value);
    }

    public string? DropDownAutomationId
    {
        get => GetValue(DropDownAutomationIdProperty);
        set => SetValue(DropDownAutomationIdProperty, value);
    }

    public string? ListAutomationId
    {
        get => GetValue(ListAutomationIdProperty);
        set => SetValue(ListAutomationIdProperty, value);
    }

    public string? NoMatchesAutomationId
    {
        get => GetValue(NoMatchesAutomationIdProperty);
        set => SetValue(NoMatchesAutomationIdProperty, value);
    }

    public bool IsExclude
    {
        get => GetValue(IsExcludeProperty);
        set => SetValue(IsExcludeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FiltersProperty)
        {
            RefreshFilterItems();
            return;
        }

        if (change.Property == WatermarkProperty ||
            change.Property == NoMatchesTextProperty ||
            change.Property == SummaryWidthProperty ||
            change.Property == SummaryMinWidthProperty ||
            change.Property == SummaryAutomationIdProperty ||
            change.Property == SearchAutomationIdProperty ||
            change.Property == DropDownAutomationIdProperty ||
            change.Property == ListAutomationIdProperty ||
            change.Property == NoMatchesAutomationIdProperty)
        {
            UpdateStaticTextAndAutomation();
            UpdateInputTextFromState();
            return;
        }

        if (change.Property == IsExcludeProperty)
        {
            UpdateExcludeClass();
            return;
        }

        if (change.Property == BoundsProperty)
        {
            UpdateInputTextFromState();
            UpdatePopupBounds();
        }

        if (change.Property == FontSizeProperty)
        {
            UpdateInputTextFromState();
            UpdatePopupBounds();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CloseDropDown();
        UnsubscribeFromFilters();
        base.OnDetachedFromVisualTree(e);
    }

    private void Input_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (PART_DropDownPopup.IsOpen)
        {
            restorePopupAfterInputDismiss = true;
            Dispatcher.UIThread.Post(
                () => restorePopupAfterInputDismiss = false,
                DispatcherPriority.Background);

            if (isSearchActive)
            {
                PART_Input.Focus();
                PART_Input.CaretIndex = PART_Input.Text?.Length ?? 0;
            }
            else
            {
                EnterSearchMode();
            }
        }
        else
        {
            if (pendingSearchAfterLightDismiss)
            {
                pendingSearchAfterLightDismiss = false;
                OpenDropDown();
                EnterSearchMode();
            }
            else
            {
                OpenDropDown();
            }
        }

        e.Handled = true;
    }

    private void Input_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!PART_DropDownPopup.IsOpen)
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                OpenDropDown();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            HandleEscape();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.F2)
        {
            EnterSearchMode();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Down or Key.Up)
        {
            MoveSelection(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
    }

    private void Input_OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (!PART_DropDownPopup.IsOpen || isSearchActive || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        EnterSearchMode(e.Text);
        e.Handled = true;
    }

    private void Input_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (isUpdatingInputText || !isSearchActive)
        {
            return;
        }

        searchText = PART_Input.Text ?? string.Empty;
        ApplySearchFilter();
    }

    private void List_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HandleEscape();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.F2)
        {
            EnterSearchMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            ToggleSelectedFilter();
            e.Handled = true;
        }
    }

    private void DropDownPopup_OnClosed(object? sender, EventArgs e)
    {
        if (!isProgrammaticClose && restorePopupAfterInputDismiss)
        {
            Dispatcher.UIThread.Post(RestorePopupAfterInputDismiss, DispatcherPriority.Input);
            return;
        }

        restorePopupAfterInputDismiss = false;
        if (!isProgrammaticClose)
        {
            pendingSearchAfterLightDismiss = true;
            Dispatcher.UIThread.Post(() => pendingSearchAfterLightDismiss = false);
        }
        else
        {
            pendingSearchAfterLightDismiss = false;
        }

        ClearOpenDropDownReference(this);
        ClearSearchState();
        UpdateInputTextFromState();
    }

    private void OpenDropDown()
    {
        CloseOpenDropDownForAnotherControl();
        pendingSearchAfterLightDismiss = false;
        ClearSearchState(updateInput: false);
        ApplySearchFilter();
        UpdatePopupBounds();

        if (!PART_DropDownPopup.IsOpen)
        {
            PART_DropDownPopup.IsOpen = true;
            Dispatcher.UIThread.Post(UpdatePopupBounds, DispatcherPriority.Loaded);
        }

        openDropDownReference = new WeakReference<EmojiFilterMultiSelectSearchBox>(this);
        UpdateInputTextFromState();
    }

    private void CloseDropDown()
    {
        var wasOpen = PART_DropDownPopup.IsOpen;
        if (wasOpen)
        {
            isProgrammaticClose = true;

            try
            {
                PART_DropDownPopup.IsOpen = false;
            }
            finally
            {
                isProgrammaticClose = false;
            }
        }

        pendingSearchAfterLightDismiss = false;
        restorePopupAfterInputDismiss = false;
        ClearOpenDropDownReference(this);
        ClearSearchState();
        UpdateInputTextFromState();
    }

    private void RestorePopupAfterInputDismiss()
    {
        restorePopupAfterInputDismiss = false;
        pendingSearchAfterLightDismiss = false;

        if (!isSearchActive ||
            PART_DropDownPopup.IsOpen ||
            TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        ApplySearchFilter();
        UpdatePopupBounds();
        PART_DropDownPopup.IsOpen = true;
        openDropDownReference = new WeakReference<EmojiFilterMultiSelectSearchBox>(this);
        UpdateInputTextFromState();
        PART_Input.Focus();
        PART_Input.CaretIndex = PART_Input.Text?.Length ?? 0;
    }

    private void EnterSearchMode(string? initialText = null)
    {
        if (!PART_DropDownPopup.IsOpen)
        {
            OpenDropDown();
        }

        isSearchActive = true;
        searchText = initialText ?? string.Empty;
        PART_Input.IsReadOnly = false;
        UpdateInputTextFromState();
        ApplySearchFilter();
        PART_Input.Focus();
        PART_Input.CaretIndex = PART_Input.Text?.Length ?? 0;
    }

    private void ClearSearchState(bool updateInput = true)
    {
        isSearchActive = false;
        searchText = string.Empty;
        PART_Input.IsReadOnly = true;
        ApplySearchFilter();

        if (updateInput)
        {
            UpdateInputTextFromState();
        }
    }

    private void HandleEscape()
    {
        if (isSearchActive && !string.IsNullOrEmpty(searchText))
        {
            searchText = string.Empty;
            UpdateInputTextFromState();
            ApplySearchFilter();
            return;
        }

        CloseDropDown();
    }

    private void MoveSelection(int delta)
    {
        if (displayedFilters.Count == 0)
        {
            return;
        }

        var nextIndex = PART_List.SelectedIndex < 0 ? 0 : PART_List.SelectedIndex + delta;
        nextIndex = Math.Clamp(nextIndex, 0, displayedFilters.Count - 1);
        PART_List.SelectedIndex = nextIndex;
        PART_List.Focus();
    }

    private void ToggleSelectedFilter()
    {
        if (PART_List.SelectedItem is not EmojiFilter filter)
        {
            return;
        }

        filter.ShowTasks = !filter.ShowTasks;
        UpdateInputTextFromState();
    }

    private void RefreshFilterItems()
    {
        UnsubscribeFromFilters();

        if (Filters is INotifyCollectionChanged collection)
        {
            subscribedCollection = collection;
            subscribedCollection.CollectionChanged += Filters_OnCollectionChanged;
        }

        selectableFilters.Clear();
        selectableFilters.AddRange(EnumerateSelectableFilters(Filters));

        foreach (var filter in selectableFilters.OfType<INotifyPropertyChanged>())
        {
            filter.PropertyChanged += Filter_OnPropertyChanged;
            subscribedFilters.Add(filter);
        }

        ApplySearchFilter();
        UpdateInputTextFromState();
    }

    private void Filters_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshFilterItems();
    }

    private void Filter_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(EmojiFilter.ShowTasks) ||
            e.PropertyName == nameof(EmojiFilter.Title) ||
            e.PropertyName == nameof(EmojiFilter.Emoji) ||
            e.PropertyName == nameof(EmojiFilter.SortText) ||
            e.PropertyName == nameof(EmojiFilter.SearchText))
        {
            ApplySearchFilter();
            UpdateInputTextFromState();
        }
    }

    private void UnsubscribeFromFilters()
    {
        if (subscribedCollection != null)
        {
            subscribedCollection.CollectionChanged -= Filters_OnCollectionChanged;
            subscribedCollection = null;
        }

        foreach (var filter in subscribedFilters)
        {
            filter.PropertyChanged -= Filter_OnPropertyChanged;
        }

        subscribedFilters.Clear();
    }

    private void ApplySearchFilter()
    {
        var query = SearchDefinition.NormalizeText(searchText.Trim());
        var matches = string.IsNullOrEmpty(query)
            ? selectableFilters
            : selectableFilters
                .Where(filter => SearchDefinition.NormalizeText(filter.SearchText).Contains(query, StringComparison.Ordinal))
                .ToList();
        var hasNoMatches = query.Length > 0 && matches.Count == 0;
        var itemsToShow = hasNoMatches ? selectableFilters : matches;

        displayedFilters.Clear();
        foreach (var filter in itemsToShow)
        {
            displayedFilters.Add(filter);
        }

        PART_NoMatchesPanel.IsVisible = hasNoMatches;
        PART_List.IsVisible = true;
        if (PART_DropDownPopup.IsOpen)
        {
            UpdatePopupBounds();
        }

        if (PART_List.SelectedIndex >= displayedFilters.Count)
        {
            PART_List.SelectedIndex = displayedFilters.Count - 1;
        }
    }

    private void UpdateStaticTextAndAutomation()
    {
        PART_Input.PlaceholderText = Watermark;
        PART_NoMatches.Text = NoMatchesText;
        AutomationProperties.SetAutomationId(PART_DropDown, DropDownAutomationId);
        AutomationProperties.SetAutomationId(PART_List, ListAutomationId);
        AutomationProperties.SetAutomationId(PART_NoMatches, NoMatchesAutomationId);
        UpdateInputAutomationId();
    }

    private void UpdateInputTextFromState()
    {
        isUpdatingInputText = true;

        try
        {
            var selectedTokens = GetSelectedSummaryTokens();
            var isEmptySummary = !isSearchActive && selectedTokens.Length == 0;
            var inputText = isSearchActive
                ? searchText
                : isEmptySummary
                    ? EmptySummaryToken
                    : BuildFittedSummary(selectedTokens);

            PART_Input.Text = inputText;
            PART_Input.PlaceholderText = isSearchActive ? Watermark : null;
            PART_Input.TextAlignment = isSearchActive || !isEmptySummary
                ? TextAlignment.Left
                : TextAlignment.Center;
            PART_Input.Padding = SummaryPadding;
            PART_Input.MinWidth = GetSummarySquareWidth();
            PART_Input.Width = GetSummaryInputWidth(inputText, isEmptySummary);
            UpdateInputAutomationId();
        }
        finally
        {
            isUpdatingInputText = false;
        }
    }

    private double GetSummaryInputWidth(string inputText, bool isEmptySummary)
    {
        var squareWidth = GetSummarySquareWidth();
        if (isEmptySummary)
        {
            return squareWidth;
        }

        if (isSearchActive)
        {
            return GetSummaryMaxWidth();
        }

        var desiredWidth = MeasureInputTextWidth(inputText) + GetSummaryTextHorizontalReserve();
        return Math.Clamp(Math.Ceiling(desiredWidth), squareWidth, GetSummaryMaxWidth());
    }

    private void UpdateInputAutomationId()
    {
        AutomationProperties.SetAutomationId(
            PART_Input,
            isSearchActive
                ? SearchAutomationId ?? SummaryAutomationId
                : SummaryAutomationId ?? SearchAutomationId);
    }

    private void UpdatePopupBounds()
    {
        var inputWidth = PART_Input.Bounds.Width > 0 ? PART_Input.Bounds.Width : PART_Input.MinWidth;
        var availableWidth = MaxPopupWidth;

        var availableHeight = MaxPopupHeight;
        if (TopLevel.GetTopLevel(this) is { } topLevel &&
            PART_Input.TranslatePoint(new Point(0, 0), topLevel) is { } topLeft)
        {
            var below = topLevel.Bounds.Height - topLeft.Y - PART_Input.Bounds.Height - PopupEdgeGap;
            var above = topLeft.Y - PopupEdgeGap;
            availableHeight = Math.Max(MinPopupHeight, Math.Min(MaxPopupHeight, Math.Max(below, above)));
            availableWidth = Math.Max(inputWidth, topLevel.Bounds.Width - (PopupEdgeGap * 2));
        }

        var popupWidth = Math.Min(MaxPopupWidth, Math.Max(MinPopupWidth, inputWidth));
        popupWidth = Math.Min(popupWidth, Math.Max(inputWidth, availableWidth));
        PART_DropDownPopup.VerticalOffset = 0;
        PART_DropDown.Width = popupWidth;
        PART_DropDown.MinWidth = inputWidth;
        PART_DropDown.MaxWidth = popupWidth;
        PART_DropDown.MaxHeight = availableHeight;
        PART_List.MaxHeight = FitListHeightToWholeRows(Math.Max(
            MinListHeight,
            availableHeight - DropDownNonListHeight - (PART_NoMatchesPanel.IsVisible ? NoMatchesPanelReservedHeight : 0)));
    }

    private double FitListHeightToWholeRows(double availableHeight)
    {
        var rowHeight = GetMeasuredOrEstimatedRowHeight();
        if (rowHeight <= 0 || availableHeight <= rowHeight)
        {
            return availableHeight;
        }

        var wholeRows = Math.Max(1, Math.Floor((availableHeight - 1) / rowHeight));
        return Math.Min(availableHeight, wholeRows * rowHeight);
    }

    private double GetMeasuredOrEstimatedRowHeight()
    {
        var measuredRowHeight = PART_List.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Where(static item => item.Bounds.Height > 0)
            .Select(static item => item.Bounds.Height)
            .DefaultIfEmpty(0)
            .Max();

        if (measuredRowHeight > 0)
        {
            return Math.Ceiling(measuredRowHeight);
        }

        return Math.Ceiling(Math.Max(28d, PART_Input.FontSize * 1.35d + 4d));
    }

    private void UpdateExcludeClass()
    {
        if (IsExclude)
        {
            if (!PART_Input.Classes.Contains("Exclude"))
            {
                PART_Input.Classes.Add("Exclude");
            }

            PART_ExcludeMarker.IsVisible = true;
            return;
        }

        PART_Input.Classes.Remove("Exclude");
        PART_ExcludeMarker.IsVisible = false;
    }

    private string[] GetSelectedSummaryTokens()
    {
        return selectableFilters
            .Where(filter => filter.ShowTasks && IsEmojiFilter(filter))
            .Select(GetSummaryToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }

    private string BuildFittedSummary(IReadOnlyList<string> selectedTokens)
    {
        var availableWidth = GetSummaryMaxWidth() - GetSummaryTextHorizontalReserve();

        for (var visibleCount = selectedTokens.Count; visibleCount >= 0; visibleCount--)
        {
            var overflowCount = selectedTokens.Count - visibleCount;
            var candidate = BuildSummaryCandidate(selectedTokens, visibleCount, overflowCount);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (FitsInputText(candidate, availableWidth))
            {
                return candidate;
            }
        }

        return $"+{selectedTokens.Count}";
    }

    private bool FitsInputText(string candidate, double availableWidth)
    {
        return MeasureInputTextWidth(candidate) <= availableWidth;
    }

    private double MeasureInputTextWidth(string candidate)
    {
        var textBlock = new TextBlock
        {
            Text = candidate,
            FontFamily = PART_Input.FontFamily,
            FontSize = PART_Input.FontSize,
            FontStyle = PART_Input.FontStyle,
            FontWeight = PART_Input.FontWeight
        };

        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return textBlock.DesiredSize.Width;
    }

    private double GetSummaryMaxWidth()
    {
        return Math.Max(
            GetSummarySquareWidth(),
            SummaryWidth > 0 && !double.IsNaN(SummaryWidth) && !double.IsInfinity(SummaryWidth)
                ? SummaryWidth
                : DefaultSummaryWidth);
    }

    private double GetSummarySquareWidth()
    {
        var currentHeight = GetCurrentInputHeight();
        return Math.Ceiling(currentHeight > 0 ? currentHeight : GetConfiguredSummaryMinWidth());
    }

    private double GetCurrentInputHeight()
    {
        if (PART_Input.Bounds.Height > 0)
        {
            return PART_Input.Bounds.Height;
        }

        if (!double.IsNaN(PART_Input.Height) && !double.IsInfinity(PART_Input.Height) && PART_Input.Height > 0)
        {
            return PART_Input.Height;
        }

        return PART_Input.DesiredSize.Height;
    }

    private double GetConfiguredSummaryMinWidth()
    {
        return SummaryMinWidth > 0 && !double.IsNaN(SummaryMinWidth) && !double.IsInfinity(SummaryMinWidth)
            ? SummaryMinWidth
            : DefaultSummaryMinWidth;
    }

    private double GetSummaryTextHorizontalReserve()
    {
        return PART_Input.Padding.Left +
               PART_Input.Padding.Right +
               PART_Input.BorderThickness.Left +
               PART_Input.BorderThickness.Right;
    }

    private static string BuildSummaryCandidate(IReadOnlyList<string> selectedTokens, int visibleCount, int overflowCount)
    {
        if (visibleCount == 0)
        {
            return overflowCount > 0 ? $"+{overflowCount}" : string.Empty;
        }

        var visibleText = string.Join(" ", selectedTokens.Take(visibleCount));
        return overflowCount > 0
            ? $"{visibleText} +{overflowCount}"
            : visibleText;
    }

    private static string GetSummaryToken(EmojiFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Emoji))
        {
            return filter.Emoji.Trim();
        }

        return (filter.Title ?? string.Empty).Trim();
    }

    private static IEnumerable<EmojiFilter> EnumerateSelectableFilters(IEnumerable? filters)
    {
        return filters?
                   .OfType<EmojiFilter>()
                   .Where(static filter => IsEmojiFilter(filter) || IsAllFilter(filter)) ??
               Enumerable.Empty<EmojiFilter>();
    }

    private void CloseOpenDropDownForAnotherControl()
    {
        if (openDropDownReference?.TryGetTarget(out var control) != true ||
            control is null)
        {
            openDropDownReference = null;
            return;
        }

        if (!ReferenceEquals(control, this))
        {
            control.CloseDropDown();
        }
    }

    private static void ClearOpenDropDownReference(EmojiFilterMultiSelectSearchBox control)
    {
        if (openDropDownReference?.TryGetTarget(out var openControl) != true ||
            openControl is null ||
            ReferenceEquals(openControl, control))
        {
            openDropDownReference = null;
        }
    }

    private static bool IsEmojiFilter(EmojiFilter filter)
    {
        return !string.IsNullOrWhiteSpace(filter.Emoji);
    }

    private static bool IsAllFilter(EmojiFilter filter)
    {
        return string.IsNullOrWhiteSpace(filter.Emoji) &&
               string.Equals(filter.Title, "All", StringComparison.Ordinal);
    }

}
