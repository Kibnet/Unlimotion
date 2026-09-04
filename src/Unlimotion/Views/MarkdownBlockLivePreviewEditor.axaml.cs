using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
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
    private MarkdownLiveBlockViewModel? dragSourceBlock;
    private MarkdownLiveBlockViewModel? dragTargetBlock;
    private Point dragStart;
    private bool dragTargetAfter;
    private bool isDraggingSelection;
    private MarkdownLiveBlockViewModel? pointerOverBlock;
    private MarkdownLiveBlockViewModel? toolbarFlyoutBlock;
    private double? preferredCaretX;
    private int? preferredCaretColumn;
    private TopLevel? pointerEventHost;

    public MarkdownBlockLivePreviewEditor()
    {
        InitializeComponent();
        AddHandler(
            InputElement.KeyDownEvent,
            OnEditorKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerPressedEvent,
            OnEditorPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AttachedToVisualTree += OnEditorAttachedToVisualTree;
        DetachedFromVisualTree += OnEditorDetachedFromVisualTree;
    }

    public event EventHandler<MarkdownLinkInvokedEventArgs>? LinkInvoked;

    public event EventHandler<BrokenTaskReferenceActionEventArgs>? BrokenTaskReferenceActionInvoked;

    private void OnEditorAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        pointerEventHost = TopLevel.GetTopLevel(this);
        pointerEventHost?.AddHandler(
            InputElement.PointerMovedEvent,
            OnEditorPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        pointerEventHost?.AddHandler(
            InputElement.PointerReleasedEvent,
            OnEditorPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnEditorDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ClearToolbarFlyoutState();
        pointerEventHost?.RemoveHandler(InputElement.PointerMovedEvent, OnEditorPointerMoved);
        pointerEventHost?.RemoveHandler(InputElement.PointerReleasedEvent, OnEditorPointerReleased);
        pointerEventHost = null;
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBox
            || (e.Source as Visual)?.GetVisualAncestors().OfType<TextBox>().Any() == true)
        {
            ResetPreferredCaretColumn();
        }

        var handle = e.Source as ToggleButton
            ?? (e.Source as Visual)?.GetVisualAncestors().OfType<ToggleButton>().FirstOrDefault();
        if (handle is not { DataContext: MarkdownLiveBlockViewModel block }
            || !string.Equals(
                AutomationProperties.GetAutomationId(handle),
                block.MoveHandleAutomationId,
                StringComparison.Ordinal)
            || DataContext is not MarkdownLivePreviewEditorViewModel editor
            || e.GetCurrentPoint(handle).Properties.PointerUpdateKind is
                PointerUpdateKind.RightButtonPressed or PointerUpdateKind.MiddleButtonPressed)
        {
            return;
        }

        var toggleSelection = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var extendSelection = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if ((!block.IsMoveSelected || toggleSelection || extendSelection)
            && !editor.SelectMoveBlock(block, toggleSelection, extendSelection))
        {
            return;
        }

        dragSourceBlock = block;
        dragTargetBlock = null;
        dragTargetAfter = false;
        isDraggingSelection = false;
        dragStart = e.GetPosition(this);
        e.Handled = true;
    }

    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        UpdatePointerOverBlock(e.GetPosition(this));

        if (dragSourceBlock is null
            || DataContext is not MarkdownLivePreviewEditorViewModel editor)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!isDraggingSelection && Math.Abs(position.X - dragStart.X) + Math.Abs(position.Y - dragStart.Y) < 6)
        {
            return;
        }

        isDraggingSelection = true;
        var targetControl = FindMoveTargetAt(position);
        var target = targetControl?.DataContext as MarkdownLiveBlockViewModel;
        if (target is null || target.IsMoveSelected || !target.IsMovable)
        {
            dragTargetBlock = null;
            editor.SetMoveDropTarget(null, after: false);
            return;
        }

        dragTargetBlock = target;
        dragTargetAfter = e.GetPosition(targetControl!).Y > targetControl!.Bounds.Height / 2;
        editor.SetMoveDropTarget(target, dragTargetAfter);
        e.Handled = true;
    }

    private async void OnEditorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (dragSourceBlock is null)
        {
            return;
        }

        var target = dragTargetBlock;
        var after = dragTargetAfter;
        var shouldMove = isDraggingSelection && target is not null;
        dragSourceBlock = null;
        dragTargetBlock = null;
        dragTargetAfter = false;
        isDraggingSelection = false;

        if (DataContext is MarkdownLivePreviewEditorViewModel editor)
        {
            editor.SetMoveDropTarget(null, after: false);
            if (shouldMove)
            {
                await editor.MoveSelectionToTargetAsync(target!, after);
            }
        }

        e.Handled = true;
    }

    private async void OnMoveHandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: MarkdownLiveBlockViewModel block }
            || DataContext is not MarkdownLivePreviewEditorViewModel editor)
        {
            return;
        }

        if (e.Key is not (Key.Up or Key.Down))
        {
            ResetPreferredCaretColumn();
        }

        if (e.Key == Key.Escape)
        {
            editor.ClearMoveSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (!block.IsMoveSelected)
            {
                editor.SelectMoveBlock(block, toggle: false, extendRange: false);
            }

            Dispatcher.UIThread.Post(
                () => FindByAutomationId<Border>(block.ContextToolbarAutomationId)?
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault()?
                    .Focus(),
                DispatcherPriority.Input);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            editor.SelectMoveBlock(
                block,
                toggle: true,
                extendRange: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        if (!block.IsMoveSelected)
        {
            editor.SelectMoveBlock(block, toggle: false, extendRange: false);
        }

        e.Handled = true;
        await editor.MoveSelectionByOffsetAsync(e.Key == Key.Up ? -1 : 1);
    }

    private async void OnMoveSelectionUpClick(object? sender, RoutedEventArgs e) =>
        await InvokeEditorActionAsync(sender, editor => editor.MoveSelectionByOffsetAsync(-1));

    private async void OnMoveSelectionDownClick(object? sender, RoutedEventArgs e) =>
        await InvokeEditorActionAsync(sender, editor => editor.MoveSelectionByOffsetAsync(1));

    private async void OnBulletedListClick(object? sender, RoutedEventArgs e) =>
        await InvokeEditorActionAsync(sender, editor => editor.TransformSelectionAsync(MarkdownBlockListStyle.Bulleted));

    private async void OnNumberedListClick(object? sender, RoutedEventArgs e) =>
        await InvokeEditorActionAsync(sender, editor => editor.TransformSelectionAsync(MarkdownBlockListStyle.Numbered));

    private async void OnChecklistClick(object? sender, RoutedEventArgs e) =>
        await InvokeEditorActionAsync(sender, editor => editor.TransformSelectionAsync(MarkdownBlockListStyle.Checklist));

    private async void OnCreateTaskClick(object? sender, RoutedEventArgs e) =>
        await InvokeEditorActionAsync(sender, editor => editor.InvokeSelectionActionAsync(MarkdownSelectionSemanticAction.Task));

    private async void OnCreateNoteClick(object? sender, RoutedEventArgs e) =>
        await InvokeEditorActionAsync(sender, editor => editor.InvokeSelectionActionAsync(MarkdownSelectionSemanticAction.Note));

    private async void OnChangeAreaClick(object? sender, RoutedEventArgs e) =>
        await InvokeEditorActionAsync(sender, editor => editor.InvokeSelectionActionAsync(MarkdownSelectionSemanticAction.Area));

    private async void OnConvertHeadingToAreaClick(object? sender, RoutedEventArgs e) =>
        await InvokeEditorActionAsync(sender, editor => editor.InvokeSelectionActionAsync(MarkdownSelectionSemanticAction.ConvertHeadingToArea));

    private void OnToolbarMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MarkdownLiveBlockViewModel block }
            || DataContext is not MarkdownLivePreviewEditorViewModel editor)
        {
            return;
        }

        if (toolbarFlyoutBlock is not null && !ReferenceEquals(toolbarFlyoutBlock, block))
        {
            editor.SetToolbarFlyoutOpen(toolbarFlyoutBlock, false);
        }

        toolbarFlyoutBlock = block;
        editor.SetToolbarFlyoutOpen(block, true);
    }

    private void OnToolbarFlyoutClosed(object? sender, EventArgs e) => ClearToolbarFlyoutState();

    private void ClearToolbarFlyoutState()
    {
        if (toolbarFlyoutBlock is not null
            && DataContext is MarkdownLivePreviewEditorViewModel editor)
        {
            editor.SetToolbarFlyoutOpen(toolbarFlyoutBlock, false);
        }

        toolbarFlyoutBlock = null;
    }

    private async Task InvokeEditorActionAsync(
        object? sender,
        Func<MarkdownLivePreviewEditorViewModel, Task> action)
    {
        if (DataContext is MarkdownLivePreviewEditorViewModel editor)
        {
            if (sender is Control { DataContext: MarkdownLiveBlockViewModel block }
                && !block.IsMoveSelected)
            {
                editor.SelectMoveBlock(block, toggle: false, extendRange: false);
            }

            await action(editor);
        }
    }

    private void OnBlockPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: MarkdownLiveBlockViewModel block }
            && DataContext is MarkdownLivePreviewEditorViewModel editor)
        {
            pointerOverBlock = block;
            editor.SetPointerOverBlock(block, true);
        }
    }

    private void OnBlockPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: MarkdownLiveBlockViewModel block }
            && DataContext is MarkdownLivePreviewEditorViewModel editor)
        {
            if (ReferenceEquals(pointerOverBlock, block))
            {
                pointerOverBlock = null;
            }

            editor.SetPointerOverBlock(block, false);
        }
    }

    private void UpdatePointerOverBlock(Point position)
    {
        if (DataContext is not MarkdownLivePreviewEditorViewModel editor)
        {
            return;
        }

        var block = FindMoveTargetAt(position)?.DataContext as MarkdownLiveBlockViewModel;
        if (ReferenceEquals(block, pointerOverBlock))
        {
            return;
        }

        if (pointerOverBlock is not null)
        {
            editor.SetPointerOverBlock(pointerOverBlock, false);
        }

        pointerOverBlock = block;
        if (pointerOverBlock is not null)
        {
            editor.SetPointerOverBlock(pointerOverBlock, true);
        }
    }

    private Control? FindMoveTargetAt(Point position)
    {
        foreach (var control in this.GetVisualDescendants().OfType<Control>())
        {
            if (control.DataContext is not MarkdownLiveBlockViewModel block
                || !string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    block.BlockAutomationId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var origin = control.TranslatePoint(default, this);
            if (origin is { } topLeft
                && new Rect(topLeft, control.Bounds.Size).Contains(position))
            {
                return control;
            }
        }

        return null;
    }

    private async void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control source
            && (source is Button || source.GetVisualAncestors().OfType<Button>().Any()))
        {
            return;
        }

        if (sender is not MarkdownBlockPreviewControl { DataContext: MarkdownLiveBlockViewModel requestedBlock } preview
            || DataContext is not MarkdownLivePreviewEditorViewModel editor
            || !requestedBlock.IsEditable)
        {
            return;
        }

        e.Handled = true;
        ResetPreferredCaretColumn();
        var relative = e.GetPosition(preview);
        var caretRatio = preview.Bounds.Width <= 1
            ? 1
            : Math.Clamp(relative.X / preview.Bounds.Width, 0, 1);
        var preferredCaretIndex = (int)Math.Round(requestedBlock.PreviewText.Length * caretRatio);
        await BeginRequestedBlockAsync(editor, requestedBlock, preferredCaretIndex);
    }

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.F2)
            || sender is not MarkdownBlockPreviewControl { DataContext: MarkdownLiveBlockViewModel requestedBlock } preview
            || DataContext is not MarkdownLivePreviewEditorViewModel editor
            || !requestedBlock.IsEditable)
        {
            return;
        }

        e.Handled = true;
        ResetPreferredCaretColumn();
        await BeginRequestedBlockAsync(editor, requestedBlock, preferredCaretIndex: null);
    }

    private async Task BeginRequestedBlockAsync(
        MarkdownLivePreviewEditorViewModel editor,
        MarkdownLiveBlockViewModel requestedBlock,
        int? preferredCaretIndex)
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
            FocusEditor(requestedBlock.EditorAutomationId, preferredCaretIndex);
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

        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await SaveWithoutLeavingAsync(editor, block, (TextBox)e.Source);
            return;
        }

        var extendsSelection = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
        if (e.Key is not (Key.Up or Key.Down) || extendsSelection)
        {
            ResetPreferredCaretColumn();
        }

        if (e.Key == Key.Back
            && ((TextBox)e.Source).SelectionStart == 0
            && ((TextBox)e.Source).SelectionEnd == 0)
        {
            if (!editor.CanMergeActiveWithPrevious())
            {
                return;
            }

            e.Handled = true;
            var merge = await editor.MergeActiveWithPreviousAsync();
            if (merge.IsMerged && merge.TargetBlock is not null)
            {
                FocusEditor(merge.TargetBlock.EditorAutomationId, merge.CaretIndex);
            }

            return;
        }

        if (!extendsSelection
            && e.Key is Key.Left or Key.Right or Key.Up or Key.Down
            && await TryMoveCaretAcrossBlockAsync(editor, block, (TextBox)e.Source, e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        var textBox = (TextBox)e.Source;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            InsertInternalLineBreak(block, textBox);
            return;
        }

        if (block.Kind == Unlimotion.Notes.Markdown.MarkdownBlockKind.FencedCode)
        {
            InsertInternalLineBreak(block, textBox);
            return;
        }

        await SplitBlockAsync(editor, block, textBox);
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!isSwitchingBlock && sender is TextBox { IsFocused: true })
        {
            ResetPreferredCaretColumn();
        }
    }

    private static void InsertInternalLineBreak(MarkdownLiveBlockViewModel block, TextBox textBox)
    {
        var continuation = GetInternalLineContinuation(block);
        if (continuation is null)
        {
            return;
        }

        var start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        var end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        var text = block.EditorText;
        var inserted = block.DocumentNewLine + continuation;
        block.EditorText = text[..start] + inserted + text[end..];
        var nextCaret = start + inserted.Length;
        Dispatcher.UIThread.Post(() =>
        {
            textBox.SelectionStart = nextCaret;
            textBox.SelectionEnd = nextCaret;
        }, DispatcherPriority.Input);
    }

    private async Task SplitBlockAsync(
        MarkdownLivePreviewEditorViewModel editor,
        MarkdownLiveBlockViewModel block,
        TextBox textBox)
    {
        var start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        var end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        var source = block.EditorText;
        var (left, right) = CreateSplitFragments(block, source, start, end);
        var sourceIndex = block.Index;
        block.EditorText = string.IsNullOrEmpty(right)
            ? left
            : left + block.DocumentNewLine + block.DocumentNewLine + right;
        isExplicitCommit = true;
        try
        {
            if (!await editor.CommitActiveAsync())
            {
                return;
            }

            var target = string.IsNullOrEmpty(right)
                ? editor.BeginSessionBlockAfter(sourceIndex)
                : editor.Blocks.FirstOrDefault(candidate => candidate.Index > sourceIndex && candidate.IsEditable);
            if (target is not null && editor.BeginEdit(target))
            {
                FocusEditor(target.EditorAutomationId, 0);
            }
        }
        finally
        {
            isExplicitCommit = false;
        }
    }

    private static string? GetInternalLineContinuation(MarkdownLiveBlockViewModel block)
    {
        if (block.Kind is Unlimotion.Notes.Markdown.MarkdownBlockKind.Heading
            or Unlimotion.Notes.Markdown.MarkdownBlockKind.AreaHeading
            or Unlimotion.Notes.Markdown.MarkdownBlockKind.HorizontalRule)
        {
            return null;
        }

        if (block.Kind == Unlimotion.Notes.Markdown.MarkdownBlockKind.BlockQuote)
        {
            return Regex.Match(block.EditorText, @"^[ \t]*(?:>\s*)+").Value;
        }

        if (block.Kind is Unlimotion.Notes.Markdown.MarkdownBlockKind.ListItem
            or Unlimotion.Notes.Markdown.MarkdownBlockKind.TaskListItem)
        {
            var indentation = Regex.Match(block.EditorText, @"^[ \t]*").Value;
            return indentation + "  ";
        }

        return string.Empty;
    }

    private static (string Left, string Right) CreateSplitFragments(
        MarkdownLiveBlockViewModel block,
        string source,
        int selectionStart,
        int selectionEnd)
    {
        var left = source[..selectionStart];
        var right = source[selectionEnd..];
        if (block.Kind == Unlimotion.Notes.Markdown.MarkdownBlockKind.AreaHeading)
        {
            return (source, string.Empty);
        }

        if (block.Kind == Unlimotion.Notes.Markdown.MarkdownBlockKind.TaskListItem)
        {
            var match = Regex.Match(source, @"^(?<indent>[ \t]*)(?<marker>[-+*])\s+\[[ xX]\]\s*");
            if (match.Success && selectionStart >= match.Length)
            {
                right = $"{match.Groups["indent"].Value}{match.Groups["marker"].Value} [ ] {right}";
            }
        }
        else if (block.Kind == Unlimotion.Notes.Markdown.MarkdownBlockKind.ListItem)
        {
            var match = Regex.Match(source, @"^[ \t]*(?:[-+*]|\d+[.)])\s+");
            if (match.Success && selectionStart >= match.Length)
            {
                right = match.Value + right;
            }
        }
        else if (block.Kind == Unlimotion.Notes.Markdown.MarkdownBlockKind.BlockQuote)
        {
            var match = Regex.Match(source, @"^[ \t]*(?:>\s*)+");
            if (match.Success && selectionStart >= match.Length)
            {
                right = match.Value + right;
            }
        }

        return (left, right);
    }

    private async Task SaveWithoutLeavingAsync(
        MarkdownLivePreviewEditorViewModel editor,
        MarkdownLiveBlockViewModel block,
        TextBox textBox)
    {
        var blockIndex = block.Index;
        var caret = textBox.SelectionStart;
        isExplicitCommit = true;
        try
        {
            if (!await editor.CommitActiveAsync())
            {
                return;
            }

            var target = editor.Blocks.FirstOrDefault(candidate => candidate.Index == blockIndex && candidate.IsEditable);
            if (target is not null && editor.BeginEdit(target))
            {
                FocusEditor(target.EditorAutomationId, caret);
            }
        }
        finally
        {
            isExplicitCommit = false;
        }
    }

    private async Task<bool> TryMoveCaretAcrossBlockAsync(
        MarkdownLivePreviewEditorViewModel editor,
        MarkdownLiveBlockViewModel block,
        TextBox textBox,
        Key key)
    {
        if (textBox.SelectionStart != textBox.SelectionEnd)
        {
            return false;
        }

        var caret = textBox.SelectionStart;
        var movingPrevious = key is Key.Left or Key.Up;
        var presenter = FindTextPresenter(textBox);
        var visualLineIndex = GetVisualLineIndex(presenter, caret);
        var visualLineCount = presenter?.TextLayout.TextLines.Count ?? 0;
        var isBoundary = key switch
        {
            Key.Left => caret == 0,
            Key.Right => caret == textBox.Text?.Length,
            Key.Up => visualLineCount > 0
                ? visualLineIndex == 0
                : caret == 0 || !(textBox.Text ?? string.Empty)[..caret].Contains('\n'),
            Key.Down => visualLineCount > 0
                ? visualLineIndex == visualLineCount - 1
                : caret == (textBox.Text?.Length ?? 0)
                  || !(textBox.Text ?? string.Empty)[caret..].Contains('\n'),
            _ => false
        };
        if (!isBoundary)
        {
            return false;
        }

        var targetBeforeCommit = movingPrevious
            ? editor.Blocks.LastOrDefault(candidate => candidate.Index < block.Index && candidate.IsEditable)
            : editor.Blocks.FirstOrDefault(candidate => candidate.Index > block.Index && candidate.IsEditable);
        if (targetBeforeCommit is null)
        {
            return false;
        }

        var targetLocator = new BlockLocator(
            targetBeforeCommit.Index,
            targetBeforeCommit.Block.Start,
            targetBeforeCommit.Block.Raw);

        if (key is Key.Up or Key.Down)
        {
            preferredCaretX ??= GetCaretX(presenter, caret);
            preferredCaretColumn ??= GetLogicalCaretColumn(textBox.Text ?? string.Empty, caret);
        }

        var sourceIndex = block.Index;
        isSwitchingBlock = true;
        try
        {
            if (!await editor.CommitActiveAsync())
            {
                return true;
            }

            var target = ResolveBlock(editor, targetLocator)
                ?? (movingPrevious
                    ? editor.Blocks.LastOrDefault(candidate => candidate.Index < sourceIndex && candidate.IsEditable)
                    : editor.Blocks.FirstOrDefault(candidate => candidate.Index > sourceIndex && candidate.IsEditable));
            if (target is null || !editor.BeginEdit(target))
            {
                return false;
            }

            if (key is Key.Up or Key.Down)
            {
                FocusEditorAtVisualPosition(
                    target.EditorAutomationId,
                    preferredCaretX,
                    preferredCaretColumn ?? 0,
                    useLastVisualLine: movingPrevious);
            }
            else
            {
                FocusEditor(target.EditorAutomationId, movingPrevious ? int.MaxValue : 0);
            }

            return true;
        }
        finally
        {
            isSwitchingBlock = false;
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

    private void FocusEditor(string automationId, int? preferredCaretIndex = null)
    {
        Dispatcher.UIThread.Post(
            () => FocusEditorWhenReady(automationId, preferredCaretIndex, attemptsRemaining: 8),
            DispatcherPriority.Loaded);
    }

    private void FocusEditorWhenReady(
        string automationId,
        int? preferredCaretIndex,
        int attemptsRemaining)
    {
        var editor = this.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(control => control.IsEffectivelyVisible
                && string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    automationId,
                    StringComparison.Ordinal));
        if (editor is null)
        {
            if (attemptsRemaining > 0)
            {
                Dispatcher.UIThread.Post(
                    () => FocusEditorWhenReady(automationId, preferredCaretIndex, attemptsRemaining - 1),
                    DispatcherPriority.Loaded);
            }

            return;
        }

        editor.Focus();
        if (preferredCaretIndex is not { } requested)
        {
            return;
        }

        var caret = Math.Clamp(requested, 0, editor.Text?.Length ?? 0);
        editor.SelectionStart = caret;
        editor.SelectionEnd = caret;
    }

    private static TextPresenter? FindTextPresenter(TextBox textBox) => textBox
        .GetVisualDescendants()
        .OfType<TextPresenter>()
        .FirstOrDefault();

    private static int GetVisualLineIndex(TextPresenter? presenter, int caret)
    {
        if (presenter?.TextLayout is not { } layout || layout.TextLines.Count == 0)
        {
            return -1;
        }

        return Math.Clamp(
            layout.GetLineIndexFromCharacterIndex(
                Math.Clamp(caret, 0, presenter.Text?.Length ?? 0),
                false),
            0,
            layout.TextLines.Count - 1);
    }

    private static double? GetCaretX(TextPresenter? presenter, int caret)
    {
        if (presenter?.TextLayout is not { } layout)
        {
            return null;
        }

        return layout.HitTestTextPosition(
            Math.Clamp(caret, 0, presenter.Text?.Length ?? 0)).X;
    }

    private void FocusEditorAtVisualPosition(
        string automationId,
        double? visualX,
        int fallbackColumn,
        bool useLastVisualLine)
    {
        Dispatcher.UIThread.Post(
            () => FocusEditorAtVisualPositionWhenReady(
                automationId,
                visualX,
                fallbackColumn,
                useLastVisualLine,
                attemptsRemaining: 8),
            DispatcherPriority.Loaded);
    }

    private void FocusEditorAtVisualPositionWhenReady(
        string automationId,
        double? visualX,
        int fallbackColumn,
        bool useLastVisualLine,
        int attemptsRemaining)
    {
        var editor = this.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(control => control.IsEffectivelyVisible
                && string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    automationId,
                    StringComparison.Ordinal));
        if (editor is null)
        {
            if (attemptsRemaining > 0)
            {
                Dispatcher.UIThread.Post(
                    () => FocusEditorAtVisualPositionWhenReady(
                        automationId,
                        visualX,
                        fallbackColumn,
                        useLastVisualLine,
                        attemptsRemaining - 1),
                    DispatcherPriority.Loaded);
            }

            return;
        }

        editor.Focus();
        var text = editor.Text ?? string.Empty;
        var presenter = FindTextPresenter(editor);
        var layout = presenter?.TextLayout;
        var caret = -1;
        if (layout is not null && layout.TextLines.Count > 0 && visualX is { } x)
        {
            var y = useLastVisualLine
                ? layout.TextLines.Sum(static line => line.Height)
                  - layout.TextLines[^1].Height / 2
                : layout.TextLines[0].Height / 2;
            caret = layout.HitTestPoint(new Point(Math.Max(0, x), Math.Max(0, y))).TextPosition;
        }

        if (caret < 0)
        {
            caret = ResolveLogicalCaretIndex(text, fallbackColumn, useLastVisualLine);
        }

        caret = Math.Clamp(caret, 0, text.Length);
        editor.SelectionStart = caret;
        editor.SelectionEnd = caret;
    }

    private static int GetLogicalCaretColumn(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        return caret - lineStart;
    }

    private static int ResolveLogicalCaretIndex(string text, int column, bool useLastLine)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lineStart = useLastLine ? normalized.LastIndexOf('\n') + 1 : 0;
        var lineEnd = normalized.IndexOf('\n', lineStart);
        if (lineEnd < 0)
        {
            lineEnd = normalized.Length;
        }

        return Math.Min(lineStart + Math.Max(0, column), lineEnd);
    }

    private void ResetPreferredCaretColumn()
    {
        preferredCaretX = null;
        preferredCaretColumn = null;
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
                   .FirstOrDefault(control => control.IsEffectivelyVisible
                       && string.Equals(
                           AutomationProperties.GetAutomationId(control),
                           automationId,
                           StringComparison.Ordinal))
               ?? this.GetVisualDescendants()
                .OfType<T>()
                .LastOrDefault(control => string.Equals(
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
