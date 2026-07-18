using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class TaskGraphWorkspaceCommandContract
{
    public static async Task<TaskGraphWorkspaceCommandScenarioResult> ExecuteTaskGraphWorkspaceCommandScenarioAsync()
    {
        var result = new TaskGraphWorkspaceCommandScenarioResult();

        var relationPickerTests = new MainControlRelationPickerUiTests();
        await relationPickerTests.TaskCardRelationEditor_AddParentFromCard_UpdatesStorage();
        result.RelationEditorAttachPassed = true;

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.MoveBlockedTaskToNewParent_WithFileStorage_ShouldBlockNewParent();
            result.MoveBlockedTaskPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CopyBlockedTaskToNewParent_WithFileStorage_ShouldBlockNewParent();
            result.CopyBlockedTaskPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.AddBlokedByLinkTask_Success(
                MainWindowViewModelFixture.BlockedTask6Id,
                MainWindowViewModelFixture.RootTask6Id);
            result.BlockLinkPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.AddReverseBlokedByLinkTask_Success(
                MainWindowViewModelFixture.RootTask7Id,
                MainWindowViewModelFixture.BlockedTask7Id);
            result.ReverseBlockLinkPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CloneTask_Success();
            result.CloneTaskPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CurrentTaskItemRemove_Success();
            result.DeleteTaskPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.SelectCurrentTaskMode_SyncsCorrectly();
            result.SelectActiveViewTaskPassed = true;
        });

        var treeCommandTests = new MainControlTreeCommandsUiTests();
        await treeCommandTests.TreeCommandUi_ShiftDelete_RemovesSelectedMainTreeItems();
        result.TreeShiftDeletePassed = true;

        treeCommandTests = new MainControlTreeCommandsUiTests();
        await treeCommandTests.TreeCommandUi_CtrlA_SelectsAllItemsInActiveTree();
        result.TreeSelectAllPassed = true;

        treeCommandTests = new MainControlTreeCommandsUiTests();
        await treeCommandTests.TreeDragUi_DragPreparation_PreservesExistingMultiSelectionVisualState();
        result.TreeDragPreparationPassed = true;

        return result;
    }

    public static async Task AssertTaskGraphWorkspaceCommandScenarioResultAsync(
        TaskGraphWorkspaceCommandScenarioResult result)
    {
        await Assert.That(result.RelationEditorAttachPassed).IsTrue();
        await Assert.That(result.MoveBlockedTaskPassed).IsTrue();
        await Assert.That(result.CopyBlockedTaskPassed).IsTrue();
        await Assert.That(result.BlockLinkPassed).IsTrue();
        await Assert.That(result.ReverseBlockLinkPassed).IsTrue();
        await Assert.That(result.CloneTaskPassed).IsTrue();
        await Assert.That(result.DeleteTaskPassed).IsTrue();
        await Assert.That(result.SelectActiveViewTaskPassed).IsTrue();
        await Assert.That(result.TreeShiftDeletePassed).IsTrue();
        await Assert.That(result.TreeSelectAllPassed).IsTrue();
        await Assert.That(result.TreeDragPreparationPassed).IsTrue();
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

internal sealed class TaskGraphWorkspaceCommandScenarioResult
{
    public bool RelationEditorAttachPassed { get; set; }

    public bool MoveBlockedTaskPassed { get; set; }

    public bool CopyBlockedTaskPassed { get; set; }

    public bool BlockLinkPassed { get; set; }

    public bool ReverseBlockLinkPassed { get; set; }

    public bool CloneTaskPassed { get; set; }

    public bool DeleteTaskPassed { get; set; }

    public bool SelectActiveViewTaskPassed { get; set; }

    public bool TreeShiftDeletePassed { get; set; }

    public bool TreeSelectAllPassed { get; set; }

    public bool TreeDragPreparationPassed { get; set; }
}
