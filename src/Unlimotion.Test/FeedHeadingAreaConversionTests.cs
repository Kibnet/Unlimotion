using System;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Areas;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public sealed class FeedHeadingAreaConversionTests
{
    [Test]
    public async Task ConvertCreatesAreaAndCanonicalHeadingAsOneRecoverableOperation()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string sourcePath = "Ежедневные/2026-08-28.md";
        const string sourceText = "# Проект\nТекст\n";
        var source = await vault.CreateAsync(sourcePath, sourceText);
        var parser = new MarkdownDocumentParser();
        var heading = parser.Parse(sourceText).Blocks[0];
        var journal = new InMemoryFeedOperationJournal();
        var service = new FeedHeadingAreaConversionService(
            vault,
            parser,
            new MarkdownMutationService(parser),
            journal);

        var result = await service.ConvertAsync(new FeedHeadingAreaConversionRequest(
            "vault1",
            "heading-area-create",
            sourcePath,
            source.Revision,
            new MarkdownBlockSelection(heading.Index, 1),
            FeedOperationHash.Compute(heading.Raw),
            "project",
            "Проект",
            null,
            CreateArea: true));

        var catalog = await new AreaCatalogStore(vault).LoadAsync();
        var updated = await vault.ReadAsync(sourcePath);
        var record = await journal.LoadAsync("vault1", "heading-area-create");
        using (Assert.Multiple())
        {
            await Assert.That(catalog.Catalog.Areas.Single().Id).IsEqualTo("project");
            await Assert.That(updated!.Text).Contains("## Проект <!-- unlimotion-area:project -->");
            await Assert.That(result.OutputBlockIndex).IsEqualTo(0);
            await Assert.That(record!.State).IsEqualTo(FeedOperationState.Completed);
            await Assert.That(record.ReviewApplied).IsTrue();
            await Assert.That(await journal.ListPendingAsync("vault1")).IsEmpty();
        }
    }

    [Test]
    public async Task ResumeAfterCatalogWriteUsesJournaledAreaIdWithoutCreatingDuplicate()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string sourcePath = "Ежедневные/2026-08-28.md";
        const string sourceText = "# Проект\nТекст\n";
        var source = await vault.CreateAsync(sourcePath, sourceText);
        var parser = new MarkdownDocumentParser();
        var heading = parser.Parse(sourceText).Blocks[0];
        var store = new AreaCatalogStore(vault);
        var catalog = new AreaCatalog();
        catalog.Areas.Add(new AreaDefinition
        {
            Id = "project",
            Name = "Проект",
            SortOrder = 0
        });
        await store.SaveAsync(catalog, expectedRevision: null);
        var journal = new InMemoryFeedOperationJournal();
        var pending = CreateRecord(
            sourcePath,
            source.Revision,
            heading,
            FeedOperationState.Pending,
            "heading-area-restart");
        await journal.SaveAsync(pending);
        var service = new FeedHeadingAreaConversionService(
            vault,
            parser,
            new MarkdownMutationService(parser),
            journal);

        _ = await service.ResumeAsync(pending);
        _ = await service.ResumeAsync((await journal.LoadAsync("vault1", pending.OperationId))!);

        var recoveredCatalog = await store.LoadAsync();
        var updated = await vault.ReadAsync(sourcePath);
        using (Assert.Multiple())
        {
            await Assert.That(recoveredCatalog.Catalog.Areas.Count).IsEqualTo(1);
            await Assert.That(Count(updated!.Text, "unlimotion-area:project")).IsEqualTo(1);
            await Assert.That((await journal.LoadAsync("vault1", pending.OperationId))!.State)
                .IsEqualTo(FeedOperationState.Completed);
        }
    }

    [Test]
    public async Task ResumeAfterMarkdownWriteOnlyCompletesJournal()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string sourcePath = "Ежедневные/2026-08-28.md";
        const string sourceText = "# Проект\nТекст\n";
        var original = await vault.CreateAsync(sourcePath, sourceText);
        var parser = new MarkdownDocumentParser();
        var heading = parser.Parse(sourceText).Blocks[0];
        var store = new AreaCatalogStore(vault);
        var catalog = new AreaCatalog();
        catalog.Areas.Add(new AreaDefinition { Id = "project", Name = "Проект", SortOrder = 0 });
        await store.SaveAsync(catalog, expectedRevision: null);
        var converted = new MarkdownMutationService(parser).ReplaceSelection(
            sourceText,
            new MarkdownBlockSelection(0, 1),
            "## Проект <!-- unlimotion-area:project -->");
        await vault.WriteAsync(sourcePath, converted, original.Revision);
        var journal = new InMemoryFeedOperationJournal();
        var pending = CreateRecord(
            sourcePath,
            original.Revision,
            heading,
            FeedOperationState.DestinationCreated,
            "heading-area-after-markdown");
        await journal.SaveAsync(pending);
        var service = new FeedHeadingAreaConversionService(
            vault,
            parser,
            new MarkdownMutationService(parser),
            journal);

        _ = await service.ResumeAsync(pending);

        var updated = await vault.ReadAsync(sourcePath);
        using (Assert.Multiple())
        {
            await Assert.That(Count(updated!.Text, "unlimotion-area:project")).IsEqualTo(1);
            await Assert.That((await journal.LoadAsync("vault1", pending.OperationId))!.State)
                .IsEqualTo(FeedOperationState.Completed);
            await Assert.That(await journal.ListPendingAsync("vault1")).IsEmpty();
        }
    }

    private static FeedOperationRecord CreateRecord(
        string sourcePath,
        string sourceRevision,
        MarkdownBlock heading,
        FeedOperationState state,
        string operationId)
    {
        const string canonical = "## Проект <!-- unlimotion-area:project -->";
        return new FeedOperationRecord(
            2,
            "vault1",
            operationId,
            FeedOperationKind.HeadingAreaConversion,
            state,
            sourcePath,
            AreaCatalogStore.RelativePath,
            null,
            sourceRevision,
            "project",
            DateTimeOffset.UtcNow,
            new FeedOperationRecoveryDescriptor(
                operationId,
                sourceRevision,
                new MarkdownBlockSelection(heading.Index, 1),
                FeedOperationHash.Compute(heading.Raw),
                string.Empty,
                FeedOperationHash.Compute(canonical),
                AreaId: "project",
                AreaName: "Проект",
                CreateArea: true,
                CanonicalReplacement: canonical));
    }

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;
}
