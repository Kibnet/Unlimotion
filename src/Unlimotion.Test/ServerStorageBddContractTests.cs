using System.Threading.Tasks;

namespace Unlimotion.Test;

public class ServerStorageBddContractTests
{
    [Test]
    public async Task ServerStorage_LoginRegisterRefreshFlow_ExposesExpectedAuthContracts()
    {
        await ServerStorageAuthContract.AssertLoginRegisterRefreshFlowExposesExpectedAuthContractsAsync();
    }

    [Test]
    public async Task ServerStorage_RefreshToken_RequiresAuthenticatedRefreshRequest()
    {
        await ServerStorageAuthContract.AssertRefreshTokenRequiresAuthenticatedRefreshRequestAsync();
    }

    [Test]
    public async Task ServerStorage_Connect_UsesLoginRegisterAndRefreshTokenFlow()
    {
        await ServerStorageAuthContract.AssertConnectUsesLoginRegisterAndRefreshTokenFlowAsync();
    }

    [Test]
    public async Task TaskService_TaskEndpoints_RequireAuthenticatedRequests()
    {
        await ServerStorageCrudRealtimeContract.AssertTaskEndpointsRequireAuthenticatedRequestsAsync();
    }

    [Test]
    public async Task TaskService_GetAllAndBulkInsert_PreserveAuthenticatedUserScope()
    {
        await ServerStorageCrudRealtimeContract.AssertGetAllAndBulkInsertPreserveAuthenticatedUserScopeAsync();
    }

    [Test]
    public async Task TaskService_GetTask_PreservesAuthenticatedUserScope()
    {
        await ServerStorageCrudRealtimeContract.AssertGetTaskPreservesAuthenticatedUserScopeAsync();
    }

    [Test]
    public async Task ServerStorage_SignalRHandlers_MapRemoteTaskUpdatesToStorageEvents()
    {
        await ServerStorageCrudRealtimeContract.AssertSignalRHandlersMapRemoteTaskUpdatesToStorageEventsAsync();
    }
}
