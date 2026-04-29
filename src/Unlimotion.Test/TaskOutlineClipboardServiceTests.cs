using System.Linq;
using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public class TaskOutlineClipboardServiceTests
{
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
    public async Task ParseOutline_ClampsIndentationJumpsToPreviousNode()
    {
        const string outline =
            "Root\n" +
            "\t\tJumped child";

        var nodes = TaskOutlineClipboardService.ParseOutline(outline);

        await Assert.That(nodes).HasSingleItem();
        await Assert.That(nodes[0].Children.Single().Title).IsEqualTo("Jumped child");
    }
}
