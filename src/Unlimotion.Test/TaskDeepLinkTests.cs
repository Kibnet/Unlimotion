using System.Threading.Tasks;
using Unlimotion;

namespace Unlimotion.Test;

public class TaskDeepLinkTests
{
    [Test]
    public async Task Parser_AcceptsCanonicalTaskLinkFromObsidian()
    {
        var parsed = TaskDeepLink.TryParse(
            "unlimotion://task/feed-12422d3acca249db950bccce95f0d723",
            out var link);

        await Assert.That(parsed).IsTrue();
        await Assert.That(link!.TaskId).IsEqualTo("feed-12422d3acca249db950bccce95f0d723");
    }

    [Test]
    [Arguments("https://task/feed-12422d3acca249db950bccce95f0d723")]
    [Arguments("unlimotion://note/feed-12422d3acca249db950bccce95f0d723")]
    [Arguments("unlimotion://task/")]
    [Arguments("unlimotion://task/first/second")]
    [Arguments("unlimotion://task/../settings")]
    [Arguments("unlimotion://task/task-id?command=delete")]
    [Arguments("unlimotion://task/task-id#fragment")]
    [Arguments("unlimotion://task/task%2Fid")]
    public async Task Parser_RejectsNonCanonicalOrUnsafeLinks(string value)
    {
        await Assert.That(TaskDeepLink.TryParse(value, out var link)).IsFalse();
        await Assert.That(link).IsNull();
    }

    [Test]
    public async Task FindInArguments_PreservesConfigArgumentsAndFindsTaskLink()
    {
        var link = TaskDeepLink.FindInArguments(
        [
            "--config=C:/Temp/Settings.json",
            "unlimotion://task/feed-example_123"
        ]);

        await Assert.That(link).IsNotNull();
        await Assert.That(link!.TaskId).IsEqualTo("feed-example_123");
    }
}
