using System;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

public class MarkdownReadingPresentationTests
{
    [Test]
    [Arguments("---\nunlimotion-id: note-123\n---\n", 0)]
    [Arguments("---\nareas: []\n---", 0)]
    [Arguments("---\nunlimotion-id: note-123\nareas: [work, personal]\n---", 2)]
    [Arguments("---\r\nunlimotion-id: 'note-123'\r\nareas:\r\n  - work\r\n  - \"Личные дела\"\r\n---\r\n", 2)]
    [Arguments("---\nareas:\n  - work\nunlimotion-id: note-123\n---", 1)]
    [Arguments("---\nunlimotion-id: note-123\nunlimotion-areas:\n  - work-id\n  - personal-id\n---\n", 2)]
    [Arguments("---\nunlimotion-id: note-123\nunlimotion-areas: []\n---\n", 0)]
    public async Task ServiceMetadata_KnownUnambiguousSubsetCanFold(string raw, int expectedAreas)
    {
        await Assert.That(MarkdownReadingPresentation.TryGetServiceFrontMatter(raw, out var count)).IsTrue();
        await Assert.That(count).IsEqualTo(expectedAreas);
    }

    [Test]
    [Arguments("---\n---")]
    [Arguments("---\n \n---")]
    [Arguments("---\nunlimotion-id: note-123\ntitle: User data\n---")]
    [Arguments("---\ncustom-areas: [work]\n---")]
    [Arguments("---\nareas: [work]\nunlimotion-areas: [personal]\n---")]
    [Arguments("---\nunlimotion-areas: [work]\nareas: [personal]\n---")]
    [Arguments("---\nareas: [broken\n---")]
    [Arguments("---\nareas: [work,]\n---")]
    [Arguments("---\nareas: []\nareas: [work]\n---")]
    [Arguments("---\nunlimotion-id:\n---")]
    [Arguments("---\nareas:\n---")]
    [Arguments("---\nareas:\n  - work\n    title: user\n---")]
    [Arguments("---\nareas: [*alias]\n---")]
    [Arguments("---\nunlimotion-id: !custom value\n---")]
    [Arguments("---\nunlimotion-id: null\n---")]
    [Arguments("---\nunlimotion-id: 'unterminated\n---")]
    [Arguments("---\nunlimotion-id: ' '\n---")]
    [Arguments("---\nareas: [work]\n---\nbody")]
    [Arguments("\n---\nareas: [work]\n---")]
    [Arguments("---\nareas: |\n  work\n---")]
    [Arguments("---\n# User commentary\nareas: [work]\n---")]
    public async Task ServiceMetadata_EmptyUnknownMalformedOrUnsupportedStaysRaw(string raw)
    {
        await Assert.That(MarkdownReadingPresentation.TryGetServiceFrontMatter(raw, out var count)).IsFalse();
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    [Arguments("# Лента\nТекст\n", "Лента", true)]
    [Arguments("\n#   Лента  заметок \n", "лента заметок.md", true)]
    [Arguments("---\nareas: [work]\n---\n\n# Лента\n", "Лента", true)]
    [Arguments("# Лента\n", "Другой файл", false)]
    [Arguments("Обычный текст\n# Лента\n", "Лента", false)]
    [Arguments("## Лента\n", "Лента", false)]
    [Arguments("# **Лента**\n", "Лента", false)]
    [Arguments("# [Лента](other.md)\n", "Лента", false)]
    [Arguments("# Лента #\n", "Лента", false)]
    [Arguments("#\n", "Лента", false)]
    [Arguments("# .md\n", ".md", false)]
    [Arguments("", "Лента", false)]
    public async Task ThematicTitle_OnlyFirstPlainH1CanReplaceShell(string raw, string displayName, bool expected)
    {
        await Assert.That(MarkdownReadingPresentation.HasMatchingThematicHeading(raw, displayName)).IsEqualTo(expected);
    }

    [Test]
    public async Task Toggle_IsPresentationOnlyAndPreservesAreaFilterAndSession()
    {
        using var editor = new MarkdownLivePreviewEditorViewModel();
        var source = new MarkdownLiveDocumentSnapshot("---\nareas: [work]\n---\nBody\n", "rev", false, "note.md");
        editor.Load(source);
        var metadata = editor.Blocks[0];
        var originalBlocks = editor.Blocks.Select(block => block.Block).ToArray();
        await Assert.That(editor.HasServiceFrontMatter).IsTrue();
        await Assert.That(editor.IsServiceFrontMatterExpanded).IsFalse();
        await Assert.That(metadata.IsPresentationVisible).IsFalse();
        await Assert.That(metadata.IsFeedFilterVisible).IsTrue();
        editor.ToggleServiceFrontMatter();
        await Assert.That(metadata.IsPresentationVisible).IsTrue();
        editor.ApplyAreaFilter([], false);
        await Assert.That(metadata.IsPresentationVisible).IsFalse();
        editor.ToggleServiceFrontMatter();
        editor.ToggleServiceFrontMatter();
        await Assert.That(metadata.IsPresentationVisible).IsFalse();
        editor.ApplyAreaFilter([], true);
        await Assert.That(metadata.IsPresentationVisible).IsTrue();
        await Assert.That(editor.Snapshot).IsSameReferenceAs(source);
        await Assert.That(editor.Blocks.Select(block => block.Block).SequenceEqual(originalBlocks)).IsTrue();
        editor.Load(source with { ExpectedRevisionHash = "rev-2" });
        await Assert.That(editor.IsServiceFrontMatterExpanded).IsTrue();
        editor.Load(source with { RelativePath = "other.md" });
        await Assert.That(editor.IsServiceFrontMatterExpanded).IsFalse();
    }

    [Test]
    public async Task MetadataDraft_ReclassifiesAndCannotBeHiddenWhileEditing()
    {
        using var editor = new MarkdownLivePreviewEditorViewModel(autosaveDelay: TimeSpan.FromDays(1));
        var source = new MarkdownLiveDocumentSnapshot("---\nareas: [work]\n---\nBody\n", "rev", false, "note.md");
        editor.CommitBlockAsync = (_, _) => Task.FromResult(MarkdownBlockCommitResult.Rejected("Read only"));
        editor.Load(source);
        editor.ToggleServiceFrontMatter();
        var metadata = editor.Blocks[0];
        await Assert.That(editor.BeginEdit(metadata)).IsTrue();
        metadata.EditorText = "---\nareas: [work, personal]\ntitle: User data\n---";
        await Assert.That(editor.HasServiceFrontMatter).IsFalse();
        await Assert.That(editor.CanToggleServiceFrontMatter).IsFalse();
        editor.ToggleServiceFrontMatter();
        await Assert.That(metadata.IsPresentationVisible).IsTrue();
        await Assert.That(metadata.IsDirty).IsTrue();
        await Assert.That(editor.Snapshot).IsSameReferenceAs(source);
        metadata.EditorText = "---\nareas: [work, personal]\n---";
        await Assert.That(editor.HasServiceFrontMatter).IsTrue();
        await Assert.That(editor.ServiceFrontMatterSummary.Contains("2", StringComparison.Ordinal)).IsTrue();
        await Assert.That(editor.CanToggleServiceFrontMatter).IsFalse();
        editor.ToggleServiceFrontMatter();
        await Assert.That(metadata.IsPresentationVisible).IsTrue();
        await Assert.That(editor.ActiveBlock).IsSameReferenceAs(metadata);
    }

    [Test]
    public async Task UnknownMetadata_BecomingKnownWhileEditingStaysVisibleThroughCommit()
    {
        using var editor = new MarkdownLivePreviewEditorViewModel(autosaveDelay: TimeSpan.FromDays(1));
        var source = new MarkdownLiveDocumentSnapshot("---\nareas: [work]\ntitle: User data\n---\nBody\n", "rev", false, "note.md");
        editor.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
        {
            Raw = patch.PatchedDocumentRaw,
            ExpectedRevisionHash = "rev-2"
        }));
        editor.Load(source);
        var metadata = editor.Blocks[0];
        await Assert.That(editor.HasServiceFrontMatter).IsFalse();
        await Assert.That(metadata.IsPresentationVisible).IsTrue();
        await Assert.That(editor.BeginEdit(metadata)).IsTrue();
        var wasHidden = false;
        metadata.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(metadata.IsPresentationVisible) && !metadata.IsPresentationVisible)
                wasHidden = true;
        };
        metadata.EditorText = "---\nareas: [work]\n---";
        await Assert.That(editor.HasServiceFrontMatter).IsTrue();
        await Assert.That(metadata.IsPresentationVisible).IsTrue();
        await Assert.That(editor.ActiveBlock).IsSameReferenceAs(metadata);
        await Assert.That(await editor.CommitActiveAsync()).IsTrue();
        await Assert.That(wasHidden).IsFalse();
        await Assert.That(editor.IsServiceFrontMatterExpanded).IsTrue();
        await Assert.That(editor.Blocks[0].IsPresentationVisible).IsTrue();
        editor.ToggleServiceFrontMatter();
        await Assert.That(editor.Blocks[0].IsPresentationVisible).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitEditOrReviewSelection_RevealsCollapsedMetadata(bool review)
    {
        using var editor = new MarkdownLivePreviewEditorViewModel();
        editor.CommitBlockAsync = (_, _) => Task.FromResult(MarkdownBlockCommitResult.Rejected("Read only"));
        var source = new MarkdownLiveDocumentSnapshot("---\nareas: [work]\n---\nBody\n", "rev", false, "note.md");
        editor.Load(source);
        var metadata = editor.Blocks[0];
        await Assert.That(metadata.IsPresentationVisible).IsFalse();
        if (review) editor.SetReviewSelection([metadata.Index], metadata.Index, metadata.Block.Raw);
        else await Assert.That(editor.BeginEdit(metadata)).IsTrue();
        await Assert.That(editor.IsServiceFrontMatterExpanded).IsTrue();
        await Assert.That(metadata.IsPresentationVisible).IsTrue();
        await Assert.That(editor.Snapshot).IsSameReferenceAs(source);
        if (review)
        {
            await Assert.That(metadata.IsReviewHighlighted).IsTrue();
            editor.ClearReviewSelection();
            await Assert.That(metadata.IsPresentationVisible).IsTrue();
        }
        else await Assert.That(editor.ActiveBlock).IsSameReferenceAs(metadata);
    }
}
