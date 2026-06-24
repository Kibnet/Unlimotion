using System.Threading.Tasks;

namespace Unlimotion.Test;

[NotInParallel("ServerStorageLiveIntegration")]
public sealed class ServerStorageLiveIntegrationTests
{
    [Test]
    public async Task ServerStorage_LiveSignalR_SaveTask_DeliversUpdateToSecondClientForSameUser()
    {
        await ServerStorageCrudRealtimeContract
            .AssertLiveSignalRSaveTaskDeliversUpdateToSecondClientForSameUserAsync();
    }

    [Test]
    public async Task ServerStorage_LiveServiceStackTaskApi_BulkInsertGetAllAndGetTask_RoundTripsAuthenticatedUserTasks()
    {
        await ServerStorageCrudRealtimeContract
            .AssertLiveServiceStackTaskApiRoundTripsAuthenticatedUserTasksAsync();
    }
}
