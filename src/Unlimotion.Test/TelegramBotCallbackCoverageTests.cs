using System.Threading.Tasks;

namespace Unlimotion.Test;

public class TelegramBotCallbackCoverageTests
{
    [Test]
    public async Task TelegramBotCallback_UnauthorizedUser_DoesNotSendTaskDataOrTouchTasks()
    {
        await TelegramCallbackCoverageContract
            .AssertUnauthorizedUserDoesNotSendTaskDataOrTouchTasksAsync();
    }

    [Test]
    public async Task TelegramBotCallback_Open_ShowsSelectedTaskForAllowedUser()
    {
        await TelegramCallbackCoverageContract
            .AssertOpenShowsSelectedTaskForAllowedUserAsync();
    }

    [Test]
    public async Task TelegramBotCallback_Status_UpdatesAndSavesSelectedTask()
    {
        await TelegramCallbackCoverageContract
            .AssertStatusUpdatesAndSavesSelectedTaskAsync();
    }

    [Test]
    public async Task TelegramBotCallback_InvalidStatus_AnswersWithoutSavingTask()
    {
        await TelegramCallbackCoverageContract
            .AssertInvalidStatusAnswersWithoutSavingTaskAsync();
    }

    [Test]
    public async Task TelegramBotCallback_Delete_DeletesTaskAndTelegramMessage()
    {
        await TelegramCallbackCoverageContract
            .AssertDeleteDeletesTaskAndTelegramMessageAsync();
    }

    [Test]
    public async Task TelegramBotCallback_CreateSubAndSibling_RecordUserStateAndAskForTitle()
    {
        await TelegramCallbackCoverageContract
            .AssertCreateSubAndSiblingRecordUserStateAndAskForTitleAsync();
    }

    [Test]
    public async Task TelegramBotCallback_Relations_ShowExistingListsAndEmptyStates()
    {
        await TelegramCallbackCoverageContract
            .AssertRelationsShowExistingListsAndEmptyStatesAsync();
    }
}
