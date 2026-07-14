using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class LocalJsonStorageContract
{
    public static async Task<LocalJsonStorageScenarioResult> ExecuteAsync()
    {
        var tests = new FileStorageTaskStatusTests();
        await tests.Save_WritesExplicitStatusHistoryAndCompletionCriteriaWithoutLegacyFields();
        return new LocalJsonStorageScenarioResult { JsonPersistsAndLoadsTask = true };
    }

    public static async Task AssertAsync(LocalJsonStorageScenarioResult result)
    {
        await Assert.That(result.JsonPersistsAndLoadsTask).IsTrue();
    }
}

internal sealed class LocalJsonStorageScenarioResult
{
    public bool JsonPersistsAndLoadsTask { get; set; }
}
