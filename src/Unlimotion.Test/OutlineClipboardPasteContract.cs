using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class OutlineClipboardPasteContract
{
    public static async Task<OutlineClipboardPasteScenarioResult> ExecuteAsync()
    {
        var serviceTests = new TaskOutlineClipboardServiceTests();
        var viewModelTests = new MainWindowViewModelTests();
        var uiTests = new MainControlTreeCommandsUiTests();
        try
        {
            await serviceTests.ParseOutline_ReadsMarkdownChecklistStatusAndDescriptions();
            await viewModelTests.PasteTaskOutline_CreatesNestedTasksUnderCurrentTask();
            await uiTests.TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask();

            return new OutlineClipboardPasteScenarioResult
            {
                MarkdownOutlineParsingPassed = true,
                PreviewAndTreeCreationPassed = true,
                OutlinePasteUiPassed = true
            };
        }
        finally
        {
            viewModelTests.Dispose();
        }
    }

    public static async Task AssertAsync(OutlineClipboardPasteScenarioResult result)
    {
        await Assert.That(result.MarkdownOutlineParsingPassed).IsTrue();
        await Assert.That(result.PreviewAndTreeCreationPassed).IsTrue();
        await Assert.That(result.OutlinePasteUiPassed).IsTrue();
    }
}

internal sealed class OutlineClipboardPasteScenarioResult
{
    public bool MarkdownOutlineParsingPassed { get; set; }

    public bool PreviewAndTreeCreationPassed { get; set; }

    public bool OutlinePasteUiPassed { get; set; }
}
