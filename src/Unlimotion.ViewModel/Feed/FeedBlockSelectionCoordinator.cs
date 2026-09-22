using System;
using System.Collections.Generic;
using System.Linq;

namespace Unlimotion.ViewModel.Feed;

/// <summary>Owns selection across editors, independently of recycled day views.</summary>
public sealed class FeedBlockSelectionCoordinator(Func<IEnumerable<MarkdownLivePreviewEditorViewModel>> editors,
    Func<IEnumerable<MarkdownLivePreviewEditorViewModel>>? visibleEditors = null)
{
    private MarkdownLiveBlockViewModel? anchor;
    private bool updating;

    public bool IsUpdating => updating;
    public bool SpansDocuments => editors().Distinct().Count(editor => editor.HasMoveSelection) > 1;
    public string SelectedMarkdown => string.Join(Environment.NewLine,
        editors().Distinct().Where(editor => editor.HasMoveSelection).Select(editor => editor.SelectedBlockMarkdown));

    public bool Select(MarkdownLiveBlockViewModel block, bool toggle, bool extend)
    {
        if (updating) return false;
        updating = true;
        try
        {
            var all = editors().Distinct().ToArray();
            if (!toggle) foreach (var editor in all) editor.ClearMoveSelection();
            if (extend && anchor is not null)
            {
                var visible = (visibleEditors?.Invoke() ?? all).SelectMany(editor => editor.Blocks)
                    .Where(candidate => candidate.IsMovable && candidate.IsPresentationVisible).ToArray();
                var start = Array.IndexOf(visible, anchor);
                var end = Array.IndexOf(visible, block);
                if (start >= 0 && end >= 0)
                {
                    foreach (var selected in visible.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1))
                        if (!selected.IsMoveSelected) selected.Owner.SelectMoveBlock(selected, true, false);
                    return true;
                }
            }
            anchor = block;
            return block.Owner.SelectMoveBlock(block, toggle, false);
        }
        finally { updating = false; Notify(); }
    }

    public void Clear()
    {
        if (updating) return;
        updating = true;
        try
        {
            anchor = null;
            foreach (var editor in editors().Distinct()) editor.ClearMoveSelection();
        }
        finally { updating = false; Notify(); }
    }

    private void Notify()
    {
        foreach (var editor in editors().Distinct()) editor.RefreshSelectionAvailability();
    }

    public void Remove(MarkdownLivePreviewEditorViewModel editor)
    {
        if (updating) return;
        updating = true;
        try
        {
            if (ReferenceEquals(anchor?.Owner, editor)) anchor = null;
            editor.ClearMoveSelection();
        }
        finally { updating = false; Notify(); }
    }
}
