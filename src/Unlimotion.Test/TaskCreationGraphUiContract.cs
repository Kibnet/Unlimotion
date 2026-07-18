using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class TaskCreationGraphUiContract
{
    public static async Task<TaskCreationGraphScenarioResult> ExecuteTaskCreationGraphScenarioAsync()
    {
        var result = new TaskCreationGraphScenarioResult();

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CreateRootTask_Success();
            result.RootCreationPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CreateSiblingTask_Success(MainWindowViewModelFixture.SubTask22Id);
            result.SiblingCreationPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CreateBlockedSibling_Success(MainWindowViewModelFixture.RootTask1Id);
            result.BlockedSiblingCreationPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CreateInnerTask_Success(MainWindowViewModelFixture.RootTask1Id);
            result.InnerCreationPassed = true;
        });

        var treeCommandTests = new MainControlTreeCommandsUiTests();
        await treeCommandTests.TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask();
        result.TreeUiCreationUnderSelectedTaskPassed = true;

        return result;
    }

    public static async Task AssertTaskCreationGraphScenarioResultAsync(TaskCreationGraphScenarioResult result)
    {
        await Assert.That(result.RootCreationPassed).IsTrue();
        await Assert.That(result.SiblingCreationPassed).IsTrue();
        await Assert.That(result.BlockedSiblingCreationPassed).IsTrue();
        await Assert.That(result.InnerCreationPassed).IsTrue();
        await Assert.That(result.TreeUiCreationUnderSelectedTaskPassed).IsTrue();
    }

    private static async Task RunMainWindowViewModelTestAsync(Func<MainWindowViewModelTests, Task> test)
    {
        var tests = new MainWindowViewModelTests();

        try
        {
            await test(tests);
        }
        finally
        {
            await tests.CleanupFixtureAsync();
        }
    }
}

internal sealed class TaskCreationGraphScenarioResult
{
    public bool RootCreationPassed { get; set; }

    public bool SiblingCreationPassed { get; set; }

    public bool BlockedSiblingCreationPassed { get; set; }

    public bool InnerCreationPassed { get; set; }

    public bool TreeUiCreationUnderSelectedTaskPassed { get; set; }
}
