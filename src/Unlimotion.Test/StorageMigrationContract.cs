using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class StorageMigrationContract
{
    public static async Task<StorageMigrationScenarioResult> ExecuteAsync()
    {
        var result = new StorageMigrationScenarioResult();
        var storageTests = new UnifiedTaskStorageMigrationRegressionTests();

        await storageTests.UnifiedTaskStorage_Init_ShouldRepairReverseLinks_WhenMigrationReportAlreadyExists();
        result.ReverseLinksRepaired = true;

        await storageTests.UnifiedTaskStorage_Init_ShouldRecalculateAvailability_WhenReverseLinksWereRepaired();
        result.AvailabilityRecalculated = true;

        var statusTests = new TaskStatusMigrationTests();
        await statusTests.Init_OldActiveTask_MigratesToNotReadyAndRemovesLegacyFields();
        result.StatusModelMigrated = true;

        return result;
    }

    public static async Task AssertAsync(StorageMigrationScenarioResult result)
    {
        await Assert.That(result.ReverseLinksRepaired).IsTrue();
        await Assert.That(result.AvailabilityRecalculated).IsTrue();
        await Assert.That(result.StatusModelMigrated).IsTrue();
    }
}

internal sealed class StorageMigrationScenarioResult
{
    public bool ReverseLinksRepaired { get; set; }

    public bool AvailabilityRecalculated { get; set; }

    public bool StatusModelMigrated { get; set; }
}
