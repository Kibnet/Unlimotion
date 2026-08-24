using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views;

public partial class MarkdownBlockLivePreviewEditor : UserControl
{
    private bool isSwitchingBlock;
    private bool isExplicitCommit;

    public MarkdownBlockLivePreviewEditor()
    {
        InitializeComponent();
        AddHandler(
            InputElement.KeyDownEvent,
            OnEditorKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    public event EventHandler<MarkdownLinkInvokedEventArgs>? LinkInvoked;

    public event EventHandler<BrokenTaskReferenceActionEventArgs>? BrokenTaskReferenceActionInvoked;

    private async void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control source
            && (source is Button || source.GetVisualAncestors().OfType<Button>().Any()))
        {
            return;
        }

        if (sender is not MarkdownBlockPreviewControl { DataContext: MarkdownLiveBlockViewModel requestedBlock }
            || DataContext is not MarkdownLivePreviewEditorViewModel editor
            || !requestedBlock.IsEditable)
        {
            return;
        }

        e.Handled = true;
        await BeginRequestedBlockAsync(editor, requestedBlock);
    }

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.F2)
            || sender is not MarkdownBlockPreviewControl { DataContext: MarkdownLiveBlockViewModel requestedBlock }
            || DataContext is not MarkdownLivePreviewEditorViewModel editor
            || !requestedBlock.IsEditable)
        {
            return;
        }

        e.Handled = true;
        await BeginRequestedBlockAsync(editor, requestedBlock);
    }

    private async Task BeginRequestedBlockAsync(
        MarkdownLivePreviewEditorViewModel editor,
        MarkdownLiveBlockViewModel requestedBlock)
    {
        if (editor.ActiveBlock is not null && !ReferenceEquals(editor.ActiveBlock, requestedBlock))
        {
            var locator = new BlockLocator(
                requestedBlock.Index,
                requestedBlock.Block.Start,
                requestedBlock.Block.Raw);
            isSwitchingBlock = true;
            try
            {
                if (!await editor.CommitActiveAsync())
                {
                    return;
                }

                requestedBlock = ResolveBlock(editor, locator) ?? requestedBlock;
            }
            finally
            {
                isSwitchingBlock = false;
            }
        }

        if (editor.BeginEdit(requestedBlock))
        {
            FocusEditor(requestedBlock.EditorAutomationId);
        }
    }

    private async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox { DataContext: MarkdownLiveBlockViewModel block }
            || DataContext is not MarkdownLivePreviewEditorViewModel editor)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            editor.CancelActiveEdit();
            FocusPreview(block.PreviewAutomationId);
            return;
        }

        if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        e.Handled = true;
        isExplicitCommit = true;
        try
        {
            if (await editor.CommitActiveAsync())
            {
                FocusPreview(block.PreviewAutomationId);
            }
        }
        finally
        {
            isExplicitCommit = false;
        }
    }

    private async void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (isSwitchingBlock
            || isExplicitCommit
            || sender is not TextBox { DataContext: MarkdownLiveBlockViewModel block }
            || block.IsCommitInProgress
            || !block.IsEditing
            || DataContext is not MarkdownLivePreviewEditorViewModel editor)
        {
            return;
        }

        await editor.CommitActiveAsync();
    }

    private void OnLinkInvoked(object? sender, MarkdownLinkInvokedEventArgs e)
    {
        LinkInvoked?.Invoke(this, e);
    }

    private void OnBrokenTaskReferenceActionInvoked(
        object? sender,
        BrokenTaskReferenceActionEventArgs e)
    {
        BrokenTaskReferenceActionInvoked?.Invoke(this, e);
    }

    private void FocusEditor(string automationId)
    {
        Dispatcher.UIThread.Post(
            () => FindByAutomationId<TextBox>(automationId)?.Focus(),
            DispatcherPriority.Input);
    }

    private void FocusPreview(string automationId)
    {
        Dispatcher.UIThread.Post(
            () => FindByAutomationId<Control>(automationId)?.Focus(),
            DispatcherPriority.Input);
    }

    private T? FindByAutomationId<T>(string automationId)
        where T : Control
    {
        return this.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => string.Equals(
                AutomationProperties.GetAutomationId(control),
                automationId,
                StringComparison.Ordinal));
    }

    private static MarkdownLiveBlockViewModel? ResolveBlock(
        MarkdownLivePreviewEditorViewModel editor,
        BlockLocator locator)
    {
        return editor.Blocks
            .Where(block => string.Equals(block.Block.Raw, locator.Raw, StringComparison.Ordinal))
            .OrderBy(block => Math.Abs(block.Index - locator.Index))
            .ThenBy(block => Math.Abs(block.Block.Start - locator.Start))
            .FirstOrDefault();
    }

    private sealed record BlockLocator(int Index, int Start, string Raw);
}
