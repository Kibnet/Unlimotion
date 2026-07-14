using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class OutlineClipboardCopyContract
{
    public static async Task<OutlineClipboardCopyScenarioResult> ExecuteAsync()
    {
        var serviceTests = new TaskOutlineClipboardServiceTests();
        var viewModelTests = new MainWindowViewModelTests();
        var uiTests = new MainControlTreeCommandsUiTests();
        try
        {
            await serviceTests.BuildOutline_MarkdownWithDescriptions_UsesChecklistAndIndentedDescriptions();
            await viewModelTests.CopyTaskOutline_UsesMarkdownAndDescriptionSettings();
            await uiTests.TreeCommandUi_CopyTaskOutline_HotkeyAndContextMenu_Work();

            return new OutlineClipboardCopyScenarioResult
            {
                MarkdownDescriptionFormatPassed = true,
                ViewModelSettingsPassed = true,
                OutlineCopyUiPassed = true
            };
        }
        finally
        {
            viewModelTests.Dispose();
        }
    }

    public static async Task AssertAsync(OutlineClipboardCopyScenarioResult result)
    {
        await Assert.That(result.MarkdownDescriptionFormatPassed).IsTrue();
        await Assert.That(result.ViewModelSettingsPassed).IsTrue();
        await Assert.That(result.OutlineCopyUiPassed).IsTrue();
    }
}

internal sealed class OutlineClipboardCopyScenarioResult
{
    public bool MarkdownDescriptionFormatPassed { get; set; }

    public bool ViewModelSettingsPassed { get; set; }

    public bool OutlineCopyUiPassed { get; set; }
}
