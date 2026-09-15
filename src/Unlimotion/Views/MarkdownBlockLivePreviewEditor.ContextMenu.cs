using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views;

public partial class MarkdownBlockLivePreviewEditor
{
    private bool isBlockContextMenuOpen;
    private void OnAnyBlockContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (isBlockContextMenuOpen) { e.Handled = true; return; }
        var source = e.Source as Control ?? (e.Source as Avalonia.Visual)?.GetVisualAncestors().OfType<Control>().FirstOrDefault();
        var row = source is null ? null : source.GetVisualAncestors().OfType<Control>().Prepend(source).FirstOrDefault(control =>
            control.DataContext is MarkdownLiveBlockViewModel block
            && Avalonia.Automation.AutomationProperties.GetAutomationId(control) == block.BlockAutomationId);
        if (row?.DataContext is not MarkdownLiveBlockViewModel block) return;
        var text = e.Source as TextBox ?? (e.Source as Avalonia.Visual)?.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
        OpenBlockContextMenu(row, block, text);
        e.Handled = true;
    }

    private void OpenBlockContextMenu(Control target, MarkdownLiveBlockViewModel block, TextBox? text)
    {
        if (DataContext is not MarkdownLivePreviewEditorViewModel editor) return;
        if (text is null && !block.IsMoveSelected) editor.SelectMoveBlock(block, false, false);
        var menu = new ContextMenu();
        Avalonia.Automation.AutomationProperties.SetAutomationId(menu, block.ContextToolbarAutomationId);
        void Add(string key, Func<Task> action, bool enabled = true)
        {
            var item = new MenuItem { Header = L10n.Get(key), IsEnabled = enabled };
            item.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceError("Feed context action: {0}", exception);
                    editor.ReportActionError(exception.Message);
                }
            };
            menu.Items.Add(item);
        }
        var editing = text is not null && block.IsEditable && !editor.IsMoveInProgress;
        var feedOwner = this.GetVisualAncestors().OfType<FeedControl>().FirstOrDefault()?.DataContext as FeedViewModel;
        bool MutationsAllowed() => editor.CanEdit && !editor.IsMoveInProgress
            && feedOwner?.IsBusy != true && feedOwner?.IsIdentityFrozen != true
            && feedOwner?.DocumentWorkspace.Documents.Any(document =>
                ReferenceEquals(document.MarkdownEditor, editor) && document.HasExternalChangeMessage) != true;
        void AddBlock(string key, Func<Task> action, bool enabled)
        {
            Add(key, async () =>
            {
                if (!MutationsAllowed()) return;
                if (text is not null)
                {
                    var index = block.Index;
                    if (!await editor.CommitActiveAsync()) return;
                    if (index >= editor.Blocks.Count) return;
                    editor.SelectMoveBlock(editor.Blocks[index], false, false);
                }
                await action();
            }, enabled && MutationsAllowed());
        }
        if (text is not null)
        {
            Add("Cut", () => { text.Cut(); return Task.CompletedTask; }, !text.IsReadOnly);
            Add("Copy", () => { text.Copy(); return Task.CompletedTask; });
            Add("Paste", () => { text.Paste(); return Task.CompletedTask; }, !text.IsReadOnly);
            Add("SelectAll", () => { text.SelectAll(); return Task.CompletedTask; });
            menu.Items.Add(new Separator());
        }
        else
        {
            Add("Copy", async () =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(editor.SelectionCoordinator?.SelectedMarkdown ?? editor.SelectedBlockMarkdown);
            });
            menu.Items.Add(new Separator());
        }
        var semantic = editor.CanOpenSelectionActions || editing && editor.SelectionActionAsync is not null;
        AddBlock("FeedToolbarTask", () => editor.InvokeSelectionActionAsync(MarkdownSelectionSemanticAction.Task), semantic);
        AddBlock("FeedToolbarNote", () => editor.InvokeSelectionActionAsync(MarkdownSelectionSemanticAction.Note), semantic);
        AddBlock("FeedSearchArea", () => editor.InvokeSelectionActionAsync(MarkdownSelectionSemanticAction.Area), semantic);
        var pastDay = feedOwner?.Days.Any(day => ReferenceEquals(day.MarkdownEditor, editor) && day.Date < feedOwner.EffectiveToday) == true;
        AddBlock("FeedReviewMoveToday", () => editor.InvokeSelectionActionAsync(MarkdownSelectionSemanticAction.MoveToday), semantic && pastDay);
        menu.Items.Add(new Separator());
        AddBlock("FeedBlockMoveUp", () => editor.MoveSelectionByOffsetAsync(-1), editor.CanMoveSelectionUp || editing && block.Index > 0 && editor.MoveBlocksAsync is not null);
        AddBlock("FeedBlockMoveDown", () => editor.MoveSelectionByOffsetAsync(1), editor.CanMoveSelectionDown || editing && block.Index < editor.Blocks.Count - 1 && editor.MoveBlocksAsync is not null);
        if (this.GetVisualAncestors().OfType<FeedControl>().FirstOrDefault()?.DataContext is FeedViewModel areaFeed)
        {
            var moveArea = new MenuItem { Header = L10n.Get("FeedBlockMoveToArea"), IsEnabled = MutationsAllowed()
                && (editor.CanMoveSelection || editing && editor.MoveBlocksAsync is not null) };
            foreach (var area in areaFeed.Areas)
            {
                var item = new MenuItem { Header = area.DisplayPath };
                item.Click += async (_, _) =>
                {
                    if (!MutationsAllowed()) return;
                    if (text is not null)
                    {
                        var index = block.Index;
                        if (!await editor.CommitActiveAsync() || index >= editor.Blocks.Count) return;
                        editor.SelectMoveBlock(editor.Blocks[index], false, false);
                    }
                    await editor.MoveSelectionToAreaAsync(area.Area);
                };
                moveArea.Items.Add(item);
            }
            menu.Items.Add(moveArea);
        }
        menu.Items.Add(new Separator());
        var transform = editor.CanTransformSelection || editing && block.Kind is Unlimotion.Notes.Markdown.MarkdownBlockKind.Paragraph
            or Unlimotion.Notes.Markdown.MarkdownBlockKind.ListItem or Unlimotion.Notes.Markdown.MarkdownBlockKind.TaskListItem;
        AddBlock("FeedBlockPlainText", () => editor.TransformSelectionAsync(MarkdownBlockListStyle.Plain), transform);
        AddBlock("FeedBlockBulletedList", () => editor.TransformSelectionAsync(MarkdownBlockListStyle.Bulleted), transform);
        AddBlock("FeedBlockNumberedList", () => editor.TransformSelectionAsync(MarkdownBlockListStyle.Numbered), transform);
        AddBlock("FeedBlockChecklist", () => editor.TransformSelectionAsync(MarkdownBlockListStyle.Checklist), transform);
        AddBlock("FeedBlockConvertToArea", () => editor.InvokeSelectionActionAsync(MarkdownSelectionSemanticAction.ConvertHeadingToArea),
            editor.CanConvertSelectionToArea || semantic && editing && block.Kind == Unlimotion.Notes.Markdown.MarkdownBlockKind.Heading);
        if (block.Block.AreaId is { Length: > 0 } areaId && this.GetVisualAncestors().OfType<FeedControl>().FirstOrDefault()?.DataContext is FeedViewModel feed)
        {
            menu.Items.Add(new Separator());
            Add("FeedAreaSettings", () => feed.OpenAreaSettingsAsync(areaId));
        }
        if (editor.SelectionCoordinator?.SpansDocuments == true || !editor.CanOpenSelectionActions && editor.HasMoveSelection)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = L10n.Get(editor.SelectionCoordinator?.SpansDocuments == true
                ? "FeedSelectOneNote" : "FeedSelectAdjacentBlocks"), IsEnabled = false });
        }
        target.ContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            isBlockContextMenuOpen = false;
            if (text?.IsEffectivelyVisible == true) text.Focus();
            else this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
                Avalonia.Automation.AutomationProperties.GetAutomationId(control) == block.MoveHandleAutomationId)?.Focus();
        };
        isBlockContextMenuOpen = true;
        menu.Open(target);
    }
}
