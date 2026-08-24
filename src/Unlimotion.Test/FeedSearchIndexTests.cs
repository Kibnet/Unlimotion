using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Search;

namespace Unlimotion.Test;

public class FeedSearchIndexTests
{
    [Test]
    public async Task SearchReturnsNewestDailyFragmentWithAreaAndContext()
    {
        var index = new FeedSearchIndex(new MarkdownDocumentParser());
        index.IndexMarkdown("Ежедневные/2026-08-22.md", "## Работа <!-- unlimotion-area:work -->\nСтарая идея о ленте\n");
        index.IndexMarkdown("Ежедневные/2026-08-24.md", "## Работа <!-- unlimotion-area:work -->\nКонтекст до\n\nНовая идея о ЛЕНТЕ\n\nКонтекст после\n");

        var result = index.Search(new FeedSearchQuery("идея ленте", AreaIdentity: "work"));

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].Date).IsEqualTo(new System.DateOnly(2026, 8, 24));
        await Assert.That(result[0].AreaIdentity).IsEqualTo("work");
        await Assert.That(result[0].Context.Contains("Контекст", System.StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task RenameAndDeleteRemoveStaleAnchors()
    {
        var index = new FeedSearchIndex(new MarkdownDocumentParser());
        index.IndexMarkdown("Темы/old.md", "Искомый текст\n");

        index.Rename("Темы/old.md", "Архив/new.md", "Искомый текст\n");
        var renamed = index.Search(new FeedSearchQuery("искомый"));
        index.Remove("Архив/new.md");
        var removed = index.Search(new FeedSearchQuery("искомый"));

        await Assert.That(renamed).HasSingleItem();
        await Assert.That(renamed[0].RelativePath).IsEqualTo("Архив/new.md");
        await Assert.That(removed).IsEmpty();
    }

    [Test]
    public async Task InternalSidecarsAreNeverIndexedAndTypeFilterWorks()
    {
        var index = new FeedSearchIndex(new MarkdownDocumentParser());
        index.IndexMarkdown(".unlimotion/review/event.md", "secret searchable text\n");
        index.IndexMarkdown("Темы/note.md", "searchable note\n");
        index.IndexTask("task1", "searchable task", null, ["work"]);
        index.IndexTask("task2", "searchable secondary area", null, ["home", "work"]);

        var notes = index.Search(new FeedSearchQuery("searchable", Type: FeedSearchDocumentType.Note));
        var tasks = index.Search(new FeedSearchQuery("searchable", Type: FeedSearchDocumentType.Task));

        var secondaryArea = index.Search(new FeedSearchQuery("secondary", AreaIdentity: "work", Type: FeedSearchDocumentType.Task));

        await Assert.That(index.Count).IsEqualTo(3);
        await Assert.That(notes).HasSingleItem();
        await Assert.That(tasks.Count).IsEqualTo(2);
        await Assert.That(tasks.Any(result => result.RelativePath == "task:task1")).IsTrue();
        await Assert.That(secondaryArea).HasSingleItem();
        await Assert.That(secondaryArea[0].RelativePath).IsEqualTo("task:task2");
    }

    [Test]
    public async Task SearchFiltersDailyNotesAndTasksByAreaDateAndTypeWithStableNewestFirstOrder()
    {
        var index = new FeedSearchIndex(new MarkdownDocumentParser());
        index.IndexMarkdown(
            "Ежедневные/2026-08-24.md",
            "## Работа <!-- unlimotion-area:work -->\nОбщий поисковый текст\n");
        index.IndexMarkdown(
            "Темы/заметка.md",
            "---\nunlimotion-areas:\n  - home\n  - work\n---\nОбщий поисковый текст в заметке\n",
            new System.DateTimeOffset(2026, 8, 25, 12, 0, 0, System.TimeSpan.Zero));
        index.IndexTask(
            "task-new",
            "Общий поисковый текст в задаче",
            null,
            ["work"],
            new System.DateTimeOffset(2026, 8, 26, 9, 0, 0, System.TimeSpan.Zero));

        var all = index.Search(new FeedSearchQuery("общий поисковый"));
        var work = index.Search(new FeedSearchQuery("общий", AreaIdentity: "work"));
        var homeNotes = index.Search(new FeedSearchQuery(
            "общий",
            AreaIdentity: "home",
            From: new System.DateOnly(2026, 8, 25),
            To: new System.DateOnly(2026, 8, 25),
            Type: FeedSearchDocumentType.Note));

        await Assert.That(all.Count).IsEqualTo(3);
        await Assert.That(all[0].Type).IsEqualTo(FeedSearchDocumentType.Task);
        await Assert.That(all[1].Type).IsEqualTo(FeedSearchDocumentType.Note);
        await Assert.That(all[2].Type).IsEqualTo(FeedSearchDocumentType.Daily);
        await Assert.That(work.Count).IsEqualTo(3);
        await Assert.That(homeNotes).HasSingleItem();
        await Assert.That(homeNotes[0].RelativePath).IsEqualTo("Темы/заметка.md");
        await Assert.That(homeNotes[0].Date).IsEqualTo(new System.DateOnly(2026, 8, 25));
    }

    [Test]
    public async Task EmptyAreaIdentityFiltersOnlyUnassignedEntriesWhileNullKeepsAllAreas()
    {
        var index = new FeedSearchIndex(new MarkdownDocumentParser());
        index.IndexMarkdown("Ежедневные/2026-08-24.md", "Без области searchable\n\n## Работа <!-- unlimotion-area:work -->\nС областью searchable\n");

        var all = index.Search(new FeedSearchQuery("searchable", AreaIdentity: null));
        var unassigned = index.Search(new FeedSearchQuery("searchable", AreaIdentity: string.Empty));

        await Assert.That(all.Count).IsEqualTo(2);
        await Assert.That(unassigned).HasSingleItem();
        await Assert.That(unassigned[0].Text).Contains("Без области");
    }

    [Test]
    public async Task ReplaceTasksDropsRemovedTasksAndKeepsUpdatedMetadata()
    {
        var index = new FeedSearchIndex(new MarkdownDocumentParser());
        index.ReplaceTasks(
        [
            new FeedSearchTaskDocument(
                "old-task",
                "searchable old",
                null,
                ["home"],
                new System.DateTimeOffset(2026, 8, 20, 8, 0, 0, System.TimeSpan.Zero)),
            new FeedSearchTaskDocument(
                "kept-task",
                "searchable before",
                null,
                ["work"],
                new System.DateTimeOffset(2026, 8, 21, 8, 0, 0, System.TimeSpan.Zero))
        ]);

        index.ReplaceTasks(
        [
            new FeedSearchTaskDocument(
                "kept-task",
                "searchable after",
                "updated description",
                ["home", "work"],
                new System.DateTimeOffset(2026, 8, 27, 8, 0, 0, System.TimeSpan.Zero))
        ]);

        var result = index.Search(new FeedSearchQuery("searchable", Type: FeedSearchDocumentType.Task));

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].RelativePath).IsEqualTo("task:kept-task");
        await Assert.That(result[0].Text).Contains("after");
        await Assert.That(result[0].AreaIdentities.Count).IsEqualTo(2);
        await Assert.That(result[0].Date).IsEqualTo(new System.DateOnly(2026, 8, 27));
    }

    [Test]
    public async Task ResolveCurrentAnchorFollowsUniqueMovedBlockButRejectsAmbiguousDuplicate()
    {
        var index = new FeedSearchIndex(new MarkdownDocumentParser());
        const string path = "Ежедневные/2026-08-24.md";
        index.IndexMarkdown(path, "До\n\nИскомый стабильный блок\n");
        var stale = index.Search(new FeedSearchQuery("стабильный")).Single();

        index.IndexMarkdown(path, "Новый блок перед ним\n\nДо\n\nИскомый стабильный блок\n");
        var moved = index.ResolveCurrentAnchor(stale, new FeedSearchQuery("стабильный"));

        await Assert.That(moved).IsNotNull();
        await Assert.That(moved!.BlockIndex).IsNotEqualTo(stale.BlockIndex);

        index.IndexMarkdown(path, "Искомый стабильный блок\n\nДругой\n\nИскомый стабильный блок\n");
        var ambiguous = index.ResolveCurrentAnchor(stale, new FeedSearchQuery("стабильный"));

        await Assert.That(ambiguous).IsNull();
    }

    [Test]
    public async Task EqualTimestampsUseStablePathAndBlockTieBreakers()
    {
        var index = new FeedSearchIndex(new MarkdownDocumentParser());
        var timestamp = new System.DateTimeOffset(2026, 8, 24, 12, 0, 0, System.TimeSpan.Zero);
        index.IndexTask("z-task", "stable searchable", null, [], timestamp);
        index.IndexMarkdown(
            "Темы/b.md",
            "stable searchable второй\n\nstable searchable третий\n",
            timestamp);
        index.IndexMarkdown("Темы/a.md", "stable searchable первый\n", timestamp);

        var first = index.Search(new FeedSearchQuery("stable searchable"));
        var second = index.Search(new FeedSearchQuery("stable searchable"));

        // A repeat query must preserve both document order and block order for equal timestamps.
        await Assert.That(first.Select(entry => entry.Key).SequenceEqual(
            second.Select(entry => entry.Key))).IsTrue();
        await Assert.That(first.Select(entry => entry.RelativePath).SequenceEqual(
            new[]
            {
                "task:z-task",
                "Темы/a.md",
                "Темы/b.md",
                "Темы/b.md"
            })).IsTrue();
        var bBlockIndexes = first
            .Where(entry => entry.RelativePath == "Темы/b.md")
            .Select(entry => entry.BlockIndex)
            .ToArray();
        await Assert.That(bBlockIndexes.SequenceEqual(bBlockIndexes.Order())).IsTrue();
    }
}
