using System.Linq;
using System.Threading.Tasks;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

public sealed class FeedSelectionCoordinatorTests
{
    [Test]
    public async Task SelectionAndClearCrossDays_WhileRangeMovesRemainSingleDocument()
    {
        using var first = Create("first.md");
        using var second = Create("second.md");
        var coordinator = new FeedBlockSelectionCoordinator(() => [first, second]);
        first.SelectionCoordinator = second.SelectionCoordinator = coordinator;
        first.SelectMoveBlock(first.Blocks[0], false, false);
        second.SelectMoveBlock(second.Blocks[0], true, false);
        await Assert.That(first.HasMoveSelection && second.HasMoveSelection).IsTrue();
        await Assert.That(coordinator.SpansDocuments).IsTrue();
        await Assert.That(first.CanMoveSelection || second.CanMoveSelection).IsFalse();
        second.ClearMoveSelection();
        await Assert.That(first.HasMoveSelection || second.HasMoveSelection).IsFalse();
    }

    [Test]
    public async Task PlainSelectionReplacesOtherDay_AndShiftKeepsGlobalAnchor()
    {
        using var first = Create("first.md");
        using var second = Create("second.md");
        var coordinator = new FeedBlockSelectionCoordinator(() => [first, second]);
        first.SelectionCoordinator = second.SelectionCoordinator = coordinator;
        first.SelectMoveBlock(first.Blocks[0], false, false);
        second.SelectMoveBlock(second.Blocks.Last(), false, true);
        await Assert.That(first.SelectedMoveBlockCount).IsEqualTo(2);
        await Assert.That(second.SelectedMoveBlockCount).IsEqualTo(2);
        second.SelectMoveBlock(second.Blocks[0], false, false);
        await Assert.That(first.HasMoveSelection).IsFalse();
        await Assert.That(second.SelectedMoveBlockCount).IsEqualTo(1);
    }

    [Test]
    public async Task EditingClearsSelectionInOtherDay()
    {
        using var first = Create("first.md");
        using var second = Create("second.md");
        var coordinator = new FeedBlockSelectionCoordinator(() => [first, second]);
        first.SelectionCoordinator = second.SelectionCoordinator = coordinator;
        first.SelectMoveBlock(first.Blocks[0], false, false);
        second.BeginEdit(second.Blocks[0]);
        await Assert.That(first.HasMoveSelection || second.HasMoveSelection).IsFalse();
    }

    private static MarkdownLivePreviewEditorViewModel Create(string path)
    {
        var editor = new MarkdownLivePreviewEditorViewModel();
        editor.CommitBlockAsync = (_, _) => throw new System.InvalidOperationException();
        editor.Load(new MarkdownLiveDocumentSnapshot("Первый\n\nВторой\n", "revision", false, path));
        return editor;
    }
}
