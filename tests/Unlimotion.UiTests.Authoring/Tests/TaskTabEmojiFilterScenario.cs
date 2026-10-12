using AppAutomation.TUnit;
using TUnit.Assertions;

namespace Unlimotion.UiTests.Authoring.Tests;

public enum EmojiFilterScenarioTab { AllTasks, LastCreated, LastOpened }

// The user flow and its assertions do not depend on the Headless input or pixel backend.
public interface ITaskTabEmojiFilterScenarioDriver
{
    void SelectTab(EmojiFilterScenarioTab tab);
    void Toggle(bool excluding, string emoji);
    string[] VisibleTaskIds();
    string[] SelectedEmoji(bool excluding);
    void CapturePopup(bool excluding, string name);
    void PrepareLastOpened();
    void SetWidth(double width);
    void CopyFrom(EmojiFilterScenarioTab source);
    void UndoCopy();
    void CaptureCopyPanel(string name, bool sources);
}

public static class TaskTabEmojiFilterScenario
{
    public static async Task RunAsync(ITaskTabEmojiFilterScenarioDriver driver, string firstId, string secondId)
    {
        driver.SelectTab(EmojiFilterScenarioTab.AllTasks);
        driver.Toggle(false, "🚀");
        await ExpectOnly(firstId, secondId);
        driver.CapturePopup(false, "all-selected");

        driver.SelectTab(EmojiFilterScenarioTab.LastCreated);
        await Assert.That(driver.SelectedEmoji(false)).IsEmpty();
        driver.Toggle(false, "🧰");
        await ExpectOnly(secondId, firstId);
        driver.CapturePopup(false, "last-created-independent");

        driver.SelectTab(EmojiFilterScenarioTab.AllTasks);
        await Assert.That(driver.SelectedEmoji(false)).Contains("🚀");
        await Assert.That(driver.SelectedEmoji(false).Contains("🧰")).IsFalse();
        await ExpectOnly(firstId, secondId);
        driver.CapturePopup(false, "all-returned");

        driver.PrepareLastOpened();
        driver.SelectTab(EmojiFilterScenarioTab.LastOpened);
        driver.Toggle(true, "🚀");
        await ExpectOnly(secondId, firstId);
        driver.SetWidth(390);
        driver.CapturePopup(true, "last-opened-narrow-exclude");

        driver.SelectTab(EmojiFilterScenarioTab.AllTasks);
        driver.SetWidth(900);
        driver.CaptureCopyPanel("copy-sources-wide", sources: true);
        driver.CopyFrom(EmojiFilterScenarioTab.LastCreated);
        await ExpectOnly(secondId, firstId);
        driver.CaptureCopyPanel("copy-feedback-wide", sources: false);
        driver.UndoCopy();
        await ExpectOnly(firstId, secondId);
        driver.SetWidth(390);
        driver.CaptureCopyPanel("copy-sources-narrow", sources: true);
        driver.CopyFrom(EmojiFilterScenarioTab.LastCreated);
        await ExpectOnly(secondId, firstId);
        driver.CaptureCopyPanel("copy-feedback-narrow", sources: false);
        driver.UndoCopy();
        await ExpectOnly(firstId, secondId);

        async Task ExpectOnly(string present, string absent) =>
            await UiAssert.TextEqualsAsync(() =>
            {
                var visible = driver.VisibleTaskIds();
                return (visible.Contains(present) && !visible.Contains(absent)).ToString();
            }, "True");
    }
}
