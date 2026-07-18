using System.Threading.Tasks;

namespace Unlimotion.Test;

public class TelegramBotCommandAuthorizationTests
{
    [Test]
    public async Task TelegramBotCommand_UnauthorizedUser_DoesNotSendMessagesOrQueryTasks()
    {
        await TelegramCommandAuthorizationContract
            .AssertUnauthorizedUserDoesNotSendMessagesOrQueryTasksAsync();
    }

    [Test]
    public async Task TelegramBotCommand_StartAndHelp_ReturnRussianCommandTextForAllowedUser()
    {
        await TelegramCommandAuthorizationContract
            .AssertStartAndHelpReturnRussianCommandTextForAllowedUserAsync();
    }

    [Test]
    public async Task TelegramBotCommand_SearchWithResults_ShowsTaskListForAllowedUser()
    {
        await TelegramCommandAuthorizationContract
            .AssertSearchWithResultsShowsTaskListForAllowedUserAsync();
    }

    [Test]
    public async Task TelegramBotCommand_SearchWithoutResults_SendsNotFoundMessageForAllowedUser()
    {
        await TelegramCommandAuthorizationContract
            .AssertSearchWithoutResultsSendsNotFoundMessageForAllowedUserAsync();
    }

    [Test]
    public async Task TelegramBotCommand_TaskCommand_RoutesTaskIdToResponderForAllowedUser()
    {
        await TelegramCommandAuthorizationContract
            .AssertTaskCommandRoutesTaskIdToResponderForAllowedUserAsync();
    }

    [Test]
    public async Task TelegramBotCommand_RootWithResults_ShowsRootTaskListForAllowedUser()
    {
        await TelegramCommandAuthorizationContract
            .AssertRootWithResultsShowsRootTaskListForAllowedUserAsync();
    }

    [Test]
    public async Task TelegramBotCommand_UnknownCommand_ReturnsHelpHintForAllowedUser()
    {
        await TelegramCommandAuthorizationContract
            .AssertUnknownCommandReturnsHelpHintForAllowedUserAsync();
    }
}
