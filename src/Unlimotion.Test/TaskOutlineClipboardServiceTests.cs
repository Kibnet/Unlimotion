using System.Linq;
using System.Threading.Tasks;
using Unlimotion;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public class TaskOutlineClipboardServiceTests
{
    [Test]
    public async Task BuildOutline_DefaultOptions_KeepsLegacyTabIndentedFormat()
    {
        var storage = await CreateStorageAsync();
        var root = await storage.Add();
        root.Title = "Root";
        await storage.Update(root);
        var child = await storage.AddChild(root);
        child.Title = "Child";
        await storage.Update(child);

        var outline = TaskOutlineClipboardService.BuildOutline(root);

        await Assert.That(NormalizeNewLines(outline)).IsEqualTo("Root\n\tChild");
    }

    [Test]
    public async Task BuildOutline_MarkdownWithDescriptions_UsesChecklistAndIndentedDescriptions()
    {
        var storage = await CreateStorageAsync();
        var root = await storage.Add();
        root.Title = "Root";
        root.Description = "Root description\nSecond line";
        root.IsCompleted = true;
        await storage.Update(root);
        var child = await storage.AddChild(root);
        child.Title = "Child";
        child.Description = "Child description";
        child.IsCompleted = null;
        await storage.Update(child);

        var outline = TaskOutlineClipboardService.BuildOutline(
            root,
            new TaskOutlineClipboardOptions(CopyAsMarkdown: true, CopyDescription: true));

        await Assert.That(NormalizeNewLines(outline)).IsEqualTo(
            "- [x] Root\n" +
            "    Root description\n" +
            "    Second line\n" +
            "    - [ ] Child\n" +
            "        Child description");
    }

    [Test]
    public async Task BuildOutline_PlainWithDescriptions_UsesBulletTasksToDisambiguateDescriptions()
    {
        var storage = await CreateStorageAsync();
        var root = await storage.Add();
        root.Title = "Root";
        root.Description = "Root description";
        await storage.Update(root);
        var child = await storage.AddChild(root);
        child.Title = "Child";
        await storage.Update(child);

        var outline = TaskOutlineClipboardService.BuildOutline(
            root,
            new TaskOutlineClipboardOptions(CopyAsMarkdown: false, CopyDescription: true));

        await Assert.That(NormalizeNewLines(outline)).IsEqualTo(
            "- Root\n" +
            "    Root description\n" +
            "    - Child");
    }

    [Test]
    public async Task ParseOutline_SupportsTabsSpacesAndMarkdownBullets()
    {
        const string outline =
            "- Root\n" +
            "    - Child from spaces\n" +
            "\t\t* Grandchild from tab\n" +
            "+ Sibling";

        var nodes = TaskOutlineClipboardService.ParseOutline(outline);

        await Assert.That(nodes.Count).IsEqualTo(2);
        await Assert.That(nodes[0].Title).IsEqualTo("Root");
        await Assert.That(nodes[0].Children.Single().Title).IsEqualTo("Child from spaces");
        await Assert.That(nodes[0].Children.Single().Children.Single().Title).IsEqualTo("Grandchild from tab");
        await Assert.That(nodes[1].Title).IsEqualTo("Sibling");
    }

    [Test]
    public async Task ParseOutline_ReadsMarkdownChecklistStatusAndDescriptions()
    {
        const string outline =
            "- [x] Root\n" +
            "    Root description\n" +
            "    second line\n" +
            "    - [ ] Child\n" +
            "        Child description\n" +
            "- Plain sibling";

        var nodes = TaskOutlineClipboardService.ParseOutline(outline);

        await Assert.That(nodes.Count).IsEqualTo(2);
        await Assert.That(nodes[0].Title).IsEqualTo("Root");
        await Assert.That(nodes[0].IsCompleted).IsTrue();
        await Assert.That(NormalizeNewLines(nodes[0].Description)).IsEqualTo("Root description\nsecond line");
        await Assert.That(nodes[0].Children.Single().Title).IsEqualTo("Child");
        await Assert.That(nodes[0].Children.Single().IsCompleted).IsFalse();
        await Assert.That(nodes[0].Children.Single().Description).IsEqualTo("Child description");
        await Assert.That(nodes[1].Title).IsEqualTo("Plain sibling");
        await Assert.That(nodes[1].IsCompleted).IsNull();
    }

    [Test]
    public async Task ParseOutline_LegacyPlainIndentation_RemainsTaskHierarchyNotDescriptions()
    {
        const string outline =
            "Root\n" +
            "    Child\n" +
            "        Grandchild";

        var nodes = TaskOutlineClipboardService.ParseOutline(outline);

        await Assert.That(nodes).HasSingleItem();
        await Assert.That(nodes[0].Description).IsEmpty();
        await Assert.That(nodes[0].Children.Single().Title).IsEqualTo("Child");
        await Assert.That(nodes[0].Children.Single().Description).IsEmpty();
        await Assert.That(nodes[0].Children.Single().Children.Single().Title).IsEqualTo("Grandchild");
    }

    [Test]
    public async Task ParseOutline_ClampsIndentationJumpsToPreviousNode()
    {
        const string outline =
            "Root\n" +
            "\t\tJumped child";

        var nodes = TaskOutlineClipboardService.ParseOutline(outline);

        await Assert.That(nodes).HasSingleItem();
        await Assert.That(nodes[0].Children.Single().Title).IsEqualTo("Jumped child");
    }

    [Test]
    public async Task BuildPreviewText_IncludesAllParsedTasksAndDescriptions()
    {
        const string outline =
            "- [x] Root\n" +
            "    Root description\n" +
            "    - [ ] Child";

        var nodes = TaskOutlineClipboardService.ParseOutline(outline);
        var preview = TaskOutlineClipboardService.BuildPreviewText(nodes);

        await Assert.That(TaskOutlineClipboardService.CountNodes(nodes)).IsEqualTo(2);
        await Assert.That(NormalizeNewLines(preview)).IsEqualTo(
            "- [x] Root\n" +
            "    Root description\n" +
            "    - [ ] Child");
    }

    private static async Task<UnifiedTaskStorage> CreateStorageAsync()
    {
        var storage = new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage()));
        await storage.Init();
        return storage;
    }

    private static string? NormalizeNewLines(string? text)
    {
        return text?
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
    }
}
