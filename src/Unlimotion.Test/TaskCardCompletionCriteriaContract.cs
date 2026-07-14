using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class TaskCardCompletionCriteriaContract
{
    public static async Task<TaskCardCompletionCriteriaScenarioResult> ExecuteTaskCardCompletionCriteriaScenarioAsync()
    {
        var result = new TaskCardCompletionCriteriaScenarioResult();
        var taskCardTests = new MainControlTaskCardLayoutUiTests();

        await taskCardTests.CurrentTaskCard_AddCompletionCriterion_FocusesNewCriterionTextBox();
        result.AddCriterionFocused = true;

        await taskCardTests.CurrentTaskCard_CompletionCriterionRow_UsesBorderlessCompactEditing();
        result.CriterionRowEditable = true;

        var statusIconTests = new MainControlTaskStatusIconUiTests();
        await statusIconTests.TaskItemViewModel_CompletionCriterionChange_SavesOnMainThreadAfterThrottle();
        await statusIconTests.TaskStatusPickerFlyout_EnablesCompletedOptionAfterCriterionIsSatisfied();
        result.CriterionEditPersistsAndEnablesCompleted = true;

        await taskCardTests.CurrentTaskCard_CompletedTask_DisablesCompletionCriteriaEditing();
        result.CompletedTaskLocksCriteria = true;

        return result;
    }

    public static async Task AssertTaskCardCompletionCriteriaScenarioResultAsync(
        TaskCardCompletionCriteriaScenarioResult result)
    {
        await Assert.That(result.AddCriterionFocused).IsTrue();
        await Assert.That(result.CriterionRowEditable).IsTrue();
        await Assert.That(result.CriterionEditPersistsAndEnablesCompleted).IsTrue();
        await Assert.That(result.CompletedTaskLocksCriteria).IsTrue();
    }
}

internal sealed class TaskCardCompletionCriteriaScenarioResult
{
    public bool AddCriterionFocused { get; set; }

    public bool CriterionRowEditable { get; set; }

    public bool CriterionEditPersistsAndEnablesCompleted { get; set; }

    public bool CompletedTaskLocksCriteria { get; set; }
}
