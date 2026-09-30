using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views;

public partial class MarkdownBlockLivePreviewEditor
{
    public static readonly StyledProperty<bool> WorkspaceFilterEnabledProperty =
        AvaloniaProperty.Register<MarkdownBlockLivePreviewEditor, bool>(nameof(WorkspaceFilterEnabled));
    public static readonly StyledProperty<bool> WorkspaceFilterAllProperty =
        AvaloniaProperty.Register<MarkdownBlockLivePreviewEditor, bool>(nameof(WorkspaceFilterAll), true);
    public static readonly StyledProperty<FeedAreaFilterSelection[]> WorkspaceSelectedAreasProperty =
        AvaloniaProperty.Register<MarkdownBlockLivePreviewEditor, FeedAreaFilterSelection[]>(nameof(WorkspaceSelectedAreas), []);
    public static readonly StyledProperty<IReadOnlyList<MarkdownLiveBlockViewModel>> DisplayedBlocksProperty =
        AvaloniaProperty.Register<MarkdownBlockLivePreviewEditor, IReadOnlyList<MarkdownLiveBlockViewModel>>(
            nameof(DisplayedBlocks), []);

    public bool WorkspaceFilterEnabled
    {
        get => GetValue(WorkspaceFilterEnabledProperty);
        set => SetValue(WorkspaceFilterEnabledProperty, value);
    }

    public bool WorkspaceFilterAll
    {
        get => GetValue(WorkspaceFilterAllProperty);
        set => SetValue(WorkspaceFilterAllProperty, value);
    }

    public FeedAreaFilterSelection[] WorkspaceSelectedAreas
    {
        get => GetValue(WorkspaceSelectedAreasProperty);
        set => SetValue(WorkspaceSelectedAreasProperty, value);
    }

    public IReadOnlyList<MarkdownLiveBlockViewModel> DisplayedBlocks
    {
        get => GetValue(DisplayedBlocksProperty);
        private set => SetValue(DisplayedBlocksProperty, value);
    }

    private MarkdownLivePreviewEditorViewModel? filteredEditor;

    static MarkdownBlockLivePreviewEditor()
    {
        WorkspaceFilterEnabledProperty.Changed.AddClassHandler<MarkdownBlockLivePreviewEditor>(
            (control, _) => control.RefreshDisplayedBlocks());
        WorkspaceFilterAllProperty.Changed.AddClassHandler<MarkdownBlockLivePreviewEditor>(
            (control, _) => control.RefreshDisplayedBlocks());
        WorkspaceSelectedAreasProperty.Changed.AddClassHandler<MarkdownBlockLivePreviewEditor>(
            (control, _) => control.RefreshDisplayedBlocks());
    }

    private void InitializeWorkspaceAreaFiltering()
    {
        DataContextChanged += (_, _) => ObserveFilteredEditor();
        AttachedToVisualTree += (_, _) => ObserveFilteredEditor();
        DetachedFromVisualTree += (_, _) =>
        {
            if (filteredEditor is not null)
                filteredEditor.Blocks.CollectionChanged -= OnFilteredBlocksChanged;
            filteredEditor = null;
        };
    }

    private void ObserveFilteredEditor()
    {
        if (ReferenceEquals(filteredEditor, DataContext)) return;
        if (filteredEditor is not null)
            filteredEditor.Blocks.CollectionChanged -= OnFilteredBlocksChanged;
        filteredEditor = DataContext as MarkdownLivePreviewEditorViewModel;
        if (filteredEditor is not null)
            filteredEditor.Blocks.CollectionChanged += OnFilteredBlocksChanged;
        RefreshDisplayedBlocks();
    }

    private void OnFilteredBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshDisplayedBlocks();

    private void RefreshDisplayedBlocks()
    {
        if (filteredEditor is null)
        {
            DisplayedBlocks = [];
            return;
        }
        var blocks = filteredEditor.Blocks;
        DisplayedBlocks = !WorkspaceFilterEnabled || WorkspaceFilterAll
            ? blocks.ToArray()
            : blocks.Where(block => FeedAreaPresentationFilter.IsVisible(
                block.Block, WorkspaceSelectedAreas, showAll: false)).ToArray();
    }

    private bool IsBlockVisibleInThisView(MarkdownLiveBlockViewModel block) =>
        block.IsPresentationVisible && DisplayedBlocks.Contains(block);
}
