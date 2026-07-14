using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class JsonRecoveryContract
{
    public static async Task<JsonRecoveryScenarioResult> ExecuteAsync()
    {
        var result = new JsonRecoveryScenarioResult();
        var repairingTests = new JsonRepairingReaderTests();

        await repairingTests.RepairJson_Success();
        await repairingTests.RepairInnerJson_Success();
        result.JsonWasRepaired = true;

        var storageTests = new UnifiedTaskStorageMigrationRegressionTests();
        await storageTests.UnifiedTaskStorage_Init_ShouldRepairReverseLinks_WhenMigrationReportAlreadyExists();
        result.MigrationReportsWereExcludedFromTaskLoading = true;

        return result;
    }

    public static async Task AssertAsync(JsonRecoveryScenarioResult result)
    {
        await Assert.That(result.JsonWasRepaired).IsTrue();
        await Assert.That(result.MigrationReportsWereExcludedFromTaskLoading).IsTrue();
    }
}

internal sealed class JsonRecoveryScenarioResult
{
    public bool JsonWasRepaired { get; set; }

    public bool MigrationReportsWereExcludedFromTaskLoading { get; set; }
}
