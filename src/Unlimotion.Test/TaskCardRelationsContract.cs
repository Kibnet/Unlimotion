using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class TaskCardRelationsContract
{
    public static async Task<TaskCardRelationsScenarioResult> ExecuteTaskCardRelationsScenarioAsync()
    {
        var result = new TaskCardRelationsScenarioResult();
        var relationPickerTests = new MainControlRelationPickerUiTests();

        await relationPickerTests.TaskCardRelationEditor_OpenTargetsExpectedInput(
            MainWindowViewModelFixture.BlockedTask7Id,
            "CurrentTaskParentsRelationAddButton",
            "CurrentTaskParentsRelationAddInput");
        await relationPickerTests.TaskCardRelationEditor_OpenTargetsExpectedInput(
            MainWindowViewModelFixture.RootTask1Id,
            "CurrentTaskContainingRelationAddButton",
            "CurrentTaskContainingRelationAddInput");
        await relationPickerTests.TaskCardRelationEditor_OpenTargetsExpectedInput(
            MainWindowViewModelFixture.BlockedTask7Id,
            "CurrentTaskBlockingRelationAddButton",
            "CurrentTaskBlockingRelationAddInput");
        await relationPickerTests.TaskCardRelationEditor_OpenTargetsExpectedInput(
            MainWindowViewModelFixture.RootTask7Id,
            "CurrentTaskBlockedRelationAddButton",
            "CurrentTaskBlockedRelationAddInput");
        result.AllRelationEditorRoutesOpened = true;

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CurrentItemParentsAdd_Success();
            result.ParentRelationPersisted = true;
        });
        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CurrentItemContainsAdd_Success();
            result.ContainingRelationPersisted = true;
        });
        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CurrentItemBlockedByAdd_Success();
            result.BlockedByRelationPersisted = true;
        });
        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CurrentItemBlocksAdd_Success();
            result.BlockedRelationPersisted = true;
        });

        return result;
    }

    public static async Task AssertTaskCardRelationsScenarioResultAsync(TaskCardRelationsScenarioResult result)
    {
        await Assert.That(result.AllRelationEditorRoutesOpened).IsTrue();
        await Assert.That(result.ParentRelationPersisted).IsTrue();
        await Assert.That(result.ContainingRelationPersisted).IsTrue();
        await Assert.That(result.BlockedByRelationPersisted).IsTrue();
        await Assert.That(result.BlockedRelationPersisted).IsTrue();
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
            tests.Dispose();
        }
    }
}

internal sealed class TaskCardRelationsScenarioResult
{
    public bool AllRelationEditorRoutesOpened { get; set; }

    public bool ParentRelationPersisted { get; set; }

    public bool ContainingRelationPersisted { get; set; }

    public bool BlockedByRelationPersisted { get; set; }

    public bool BlockedRelationPersisted { get; set; }
}
