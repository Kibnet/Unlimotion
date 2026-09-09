using System;
using System.ComponentModel;
using System.Linq;

namespace Unlimotion.ViewModel.Feed;

public sealed partial class FeedViewModel
{
    private FeedBlockSelectionCoordinator? selectionCoordinator;
    public FeedBlockSelectionCoordinator BlockSelection => selectionCoordinator ??= new(
        () => Days.Select(day => day.MarkdownEditor).Concat(DocumentWorkspace.Documents.Select(document => document.MarkdownEditor)),
        () => HasOpenedThematicFile ? [OpenedThematicFile!.MarkdownEditor] :
            VisibleDays.Where(day => !day.IsCollapsed).Select(day => day.MarkdownEditor));

    public void AttachPresentation()
    {
        AttachDateSettings();
        foreach (var day in Days) day.MarkdownEditor.SelectionCoordinator = BlockSelection;
        foreach (var document in DocumentWorkspace.Documents) document.MarkdownEditor.SelectionCoordinator = BlockSelection;
    }

    public async System.Threading.Tasks.Task OpenAreaSettingsAsync(string areaId)
    {
        if (AreaManagement is null) return;
        await AreaManagement.OpenAreaAsync(areaId);
    }
}
