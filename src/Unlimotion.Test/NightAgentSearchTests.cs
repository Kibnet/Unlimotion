using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unlimotion.Cli;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class NightAgentSearchTests
{
    [Test]
    public async Task ContentSearch_NormalizesUnicodeButPreservesLiteralSemantics()
    {
        var source = Source(Record("one", "Решение", "Кафе\u0301 [.*] Ёж"), Record("two", "еж", "Nothing"));
        var composed = NightAgentSearch.Search(source, "КАФÉ", ["description"]);
        await Assert.That(composed.TotalCount).IsEqualTo(0); // Latin É and Cyrillic е are different.
        var decomposed = NightAgentSearch.Search(source, "Кафе\u0301", ["description"]);
        await Assert.That(decomposed.TotalCount).IsEqualTo(1);
        await Assert.That(decomposed.Items[0].Matches[0].Normalization).IsEqualTo("NFC");
        await Assert.That(NightAgentSearch.Search(source, "[.*]", ["description"]).TotalCount).IsEqualTo(1);
        await Assert.That(NightAgentSearch.Search(source, "еж", ["description"]).TotalCount).IsEqualTo(0);
        await Assert.That(NightAgentSearch.Search(source, "Кафе\u0301 ", ["description"]).TotalCount).IsEqualTo(1);
        await Assert.That(NightAgentSearch.Search(source, " Кафе\u0301", ["description"]).TotalCount).IsEqualTo(0);
    }

    [Test]
    public async Task DescriptionSearch_HidesExecutionAndReportsMalformedMarkers()
    {
        var protectedText = "Useful " + AgentExecutionDescriptionRenderer.MarkerStart + "private secret" + AgentExecutionDescriptionRenderer.MarkerEnd;
        var source = Source(Record("ok", "Task", protectedText),
            Record("broken", "Task", AgentExecutionDescriptionRenderer.MarkerStart + "secret"));
        var result = NightAgentSearch.Search(source, "secret", ["description"]);
        await Assert.That(result.TotalCount).IsEqualTo(0);
        await Assert.That(result.SearchComplete).IsFalse();
        await Assert.That(result.Warnings.Single().TaskId).IsEqualTo("broken");
        await Assert.That(result.Warnings.Single().Kind).IsEqualTo("descriptionUnavailable");
        await Assert.That(NightAgentSearch.Search(source, "Useful", ["description"]).Items.Single().Id).IsEqualTo("ok");
    }

    [Test]
    public async Task CriteriaSearch_IncludesSatisfiedTerminalAndEmptyQueryHasNoSnippets()
    {
        var source = Source(Record("done", "Completed", "") with
        {
            Status = DomainTaskStatus.Completed,
            Criteria = [new TaskCriterionOutput { Id = "criterion", Text = "Документ готов", IsSatisfied = true }]
        });
        var result = NightAgentSearch.Search(source, "готов", ["criteria"]);
        await Assert.That(result.Items.Single().Matches.Single().CriterionId).IsEqualTo("criterion");
        await Assert.That(NightAgentSearch.Search(source, "", ["criteria"]).Items.Single().Matches).IsEmpty();
    }

    [Test]
    public async Task Snippets_AreBoundedAndDoNotSplitEmojiOrUtf16Ranges()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 210)) + "match" + new string('x', 180) + "match match match";
        var item = NightAgentSearch.Search(Source(Record("one", "Task", text)), "match", ["description"]).Items.Single();
        await Assert.That(item.Matches.Count).IsEqualTo(3);
        await Assert.That(item.MatchesTruncated).IsTrue();
        foreach (var match in item.Matches)
        {
            await Assert.That(match.MoreOccurrences).IsTrue();
            await Assert.That(match.Snippet.EnumerateRunes().Count() <= 160).IsTrue();
            await Assert.That(match.Snippet.Contains('\uFFFD')).IsFalse();
            var range = match.Ranges.Single();
            await Assert.That(match.Snippet.Substring(range.Start, range.Length)).IsEqualTo("match");
        }
        var emoji = NightAgentSearch.Search(Source(Record("emoji", "Task", "a😀b")), "😀", ["description"]).Items.Single().Matches.Single();
        await Assert.That(emoji.Ranges.Single().Length).IsEqualTo(2);
        await Assert.That(NightAgentSearch.EscapeText("line\n\u001b[31m😀")).IsEqualTo("line\\u000a\\u001b[31m😀");
    }

    [Test]
    public async Task SnapshotPaging_BindsArtifactAndEveryFilterAndKeepsTitleOrdering()
    {
        var source = Source(Record("c", "b", ""), Record("b", "a", ""), Record("a", "A", ""));
        var first = NightAgentSearch.Search(source, "", ["id", "title"], limit: 1);
        var second = NightAgentSearch.Search(source, "", ["title", "id"], limit: 1, cursor: first.NextCursor);
        var third = NightAgentSearch.Search(source, "", ["title", "id"], limit: 1, cursor: second.NextCursor);
        await Assert.That(string.Join(",", new[] { first, second, third }.SelectMany(page => page.Items).Select(item => item.Id))).IsEqualTo("a,b,c");
        await Assert.That(third.NextCursor).IsNull();
        await Assert.That(() => NightAgentSearch.Search(source, "", ["id", "title"], limit: 2, cursor: first.NextCursor)).Throws<CliException>();
        await Assert.That(() => NightAgentSearch.Search(source with { Metadata = source.Metadata with { Fingerprint = "different" } },
            "", ["id", "title"], limit: 1, cursor: first.NextCursor)).Throws<CliException>();
    }

    [Test]
    public async Task RootSearch_RequiresAllCatalogDescendantsAndRequestedSections()
    {
        var source = Source(Record("root", "Root", "")) with
        { Children = new Dictionary<string, IReadOnlyList<string>> { ["root"] = ["missingPayload"], ["missingPayload"] = [] } };
        await Assert.That(() => NightAgentSearch.Search(source, "", ["id"], ["root"])).Throws<CliException>();
        await Assert.That(() => NightAgentSearch.Search(Source() with { DetailsCaptured = false }, "", ["description"])).Throws<CliException>();
        await Assert.That(() => NightAgentSearch.Search(Source(Record("one", "Task", "") with { CriteriaCaptured = false }),
            "", ["criteria"])).Throws<CliException>();
        await Assert.That(() => NightAgentSearch.Search(source, "", ["title", "title"])).Throws<CliException>();
    }

    private static NightSearchRecord Record(string id, string title, string description) =>
        new(id, "sha256:" + new string('a', 64), title, DomainTaskStatus.Prepared, 0, true, true, description, [], true, true);
    private static NightSearchSource Source(params NightSearchRecord[] records) =>
        new(new NightSearchSourceMetadata("snapshot", "immutableArtifact", "artifact", "snapshot"),
            records.ToDictionary(record => record.Id, _ => (IReadOnlyList<string>)Array.Empty<string>()), records);
}
