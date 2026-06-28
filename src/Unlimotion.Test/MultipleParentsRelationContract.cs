using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class MultipleParentsRelationContract
{
    public static async Task<MultipleParentsRelationScenarioResult> ExecuteMultipleParentsRelationScenarioAsync()
    {
        var result = new MultipleParentsRelationScenarioResult();

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CurrentItemParentsAdd_Success();
            result.ParentRelationAddPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.CurrentItemContainsAdd_Success();
            result.ContainsRelationAddPassed = true;
        });

        await RunMainWindowViewModelTestAsync(async tests =>
        {
            await tests.MovingTaskWithTwoParentsToRootTask_Success();
            result.MultipleParentMovePassed = true;
        });

        await RunDisposableTestAsync(
            new MigrateTests(),
            async tests =>
            {
                await tests.Migrate_BuildsParentsAndNormalizesChildren();
                result.MigrationBuildsReverseParentsPassed = true;
            });

        var storageTests = new UnifiedTaskStorageMigrationRegressionTests();
        await storageTests.UnifiedTaskStorage_Init_ShouldRepairReverseLinks_WhenMigrationReportAlreadyExists();
        result.StorageRepairsReverseLinksPassed = true;

        await RunStartupProjectionTestAsync(async tests =>
        {
            await tests.TaskRelationsIndex_ShouldSynchronizeRelationCollectionsWithIds();
            result.RelationCollectionsProjectionPassed = true;
        });

        var relationPickerTests = new MainControlRelationPickerUiTests();
        await relationPickerTests.TaskCardRelationEditor_AddParentFromCard_UpdatesStorage();
        result.UiRelationEditorAddParentPassed = true;

        return result;
    }

    public static async Task AssertMultipleParentsRelationScenarioResultAsync(
        MultipleParentsRelationScenarioResult result)
    {
        await Assert.That(result.ParentRelationAddPassed).IsTrue();
        await Assert.That(result.ContainsRelationAddPassed).IsTrue();
        await Assert.That(result.MultipleParentMovePassed).IsTrue();
        await Assert.That(result.MigrationBuildsReverseParentsPassed).IsTrue();
        await Assert.That(result.StorageRepairsReverseLinksPassed).IsTrue();
        await Assert.That(result.RelationCollectionsProjectionPassed).IsTrue();
        await Assert.That(result.UiRelationEditorAddParentPassed).IsTrue();
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

    private static async Task RunStartupProjectionTestAsync(Func<StartupProjectionAndRelationsTests, Task> test)
    {
        var tests = new StartupProjectionAndRelationsTests();

        try
        {
            await test(tests);
        }
        finally
        {
            tests.Dispose();
        }
    }

    private static async Task RunDisposableTestAsync<T>(T tests, Func<T, Task> test)
        where T : IDisposable
    {
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

internal sealed class MultipleParentsRelationScenarioResult
{
    public bool ParentRelationAddPassed { get; set; }

    public bool ContainsRelationAddPassed { get; set; }

    public bool MultipleParentMovePassed { get; set; }

    public bool MigrationBuildsReverseParentsPassed { get; set; }

    public bool StorageRepairsReverseLinksPassed { get; set; }

    public bool RelationCollectionsProjectionPassed { get; set; }

    public bool UiRelationEditorAddParentPassed { get; set; }
}
