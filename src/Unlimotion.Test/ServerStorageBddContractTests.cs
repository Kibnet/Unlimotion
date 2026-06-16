using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ServiceStack;
using Unlimotion.Server.ServiceInterface;
using Unlimotion.Server.ServiceModel;

namespace Unlimotion.Test;

public class ServerStorageBddContractTests
{
    [Test]
    public async Task ServerStorage_LoginRegisterRefreshFlow_ExposesExpectedAuthContracts()
    {
        await Assert.That(FindRoute<AuthViaPassword>("/password/login", "POST")).IsNotNull();
        await Assert.That(FindRoute<RegisterNewUser>("/register", "POST")).IsNotNull();
        await Assert.That(FindRoute<PostRefreshToken>("/token/refresh", "POST")).IsNotNull();

        var login = new AuthViaPassword();
        await Assert.That(login.Login).IsEqualTo(string.Empty);
        await Assert.That(login.Password).IsEqualTo(string.Empty);

        var register = new RegisterNewUser();
        await Assert.That(register.Login).IsEqualTo(string.Empty);
        await Assert.That(register.Password).IsEqualTo(string.Empty);
        await Assert.That(register.UserName).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ServerStorage_RefreshToken_RequiresAuthenticatedRefreshRequest()
    {
        MethodInfo refresh = GetServiceMethod<AuthService, PostRefreshToken>("Post");

        await Assert.That(refresh.GetCustomAttribute<AuthenticateAttribute>()).IsNotNull();
    }

    [Test]
    public async Task ServerStorage_Connect_UsesLoginRegisterAndRefreshTokenFlow()
    {
        string source = await ReadRepoFileAsync("src", "Unlimotion", "ServerStorage.cs");

        await Assert.That(source).Contains("serviceClient.PostAsync(new AuthViaPassword");
        await Assert.That(source).Contains("await RefreshToken(settings, configuration!)");
        await Assert.That(source).Contains("await RegisterUser().ConfigureAwait(false)");
        await Assert.That(source).Contains("settings.RefreshToken = tokens.RefreshToken");
        await Assert.That(source).Contains("settings.RefreshToken = tokenResult.RefreshToken");
    }

    [Test]
    public async Task TaskService_TaskEndpoints_RequireAuthenticatedRequests()
    {
        MethodInfo getAll = GetServiceMethod<TaskService, GetAllTasks>("Get");
        MethodInfo getOne = GetServiceMethod<TaskService, GetTask>("GetAsync");
        MethodInfo bulkInsert = GetServiceMethod<TaskService, BulkInsertTasks>("Post");

        await Assert.That(getAll.GetCustomAttribute<AuthenticateAttribute>()).IsNotNull();
        await Assert.That(getOne.GetCustomAttribute<AuthenticateAttribute>()).IsNotNull();
        await Assert.That(bulkInsert.GetCustomAttribute<AuthenticateAttribute>()).IsNotNull();
    }

    [Test]
    public async Task TaskService_GetAllAndBulkInsert_PreserveAuthenticatedUserScope()
    {
        string source = await ReadRepoFileAsync("src", "Unlimotion.Server.ServiceInterface", "TaskService.cs");

        await Assert.That(source).Contains("var uid = session.UserAuthId");
        await Assert.That(source).Contains(".Where(chat => chat.UserId == uid)");
        await Assert.That(source).Contains("task.UserId = uid");
        await Assert.That(source).Contains("private const string TaskPrefix = \"TaskItem/\"");
        await Assert.That(source).Contains("task.Id = $\"{TaskPrefix}{task.Id}\"");
    }

    [Test]
    public async Task TaskService_GetTask_PreservesAuthenticatedUserScope()
    {
        string source = await ReadRepoFileAsync("src", "Unlimotion.Server.ServiceInterface", "TaskService.cs");
        string methodSource = ExtractMethodSource(
            source,
            "public async Task<TaskItemMold> GetAsync(GetTask request)",
            "public async Task Post(BulkInsertTasks request)");

        await Assert.That(methodSource).Contains("var uid = session.UserAuthId");
        await Assert.That(methodSource).Contains(".Where(chat => chat.Id == decodedId && chat.UserId == uid)");
    }

    [Test]
    public async Task ServerStorage_SignalRHandlers_MapRemoteTaskUpdatesToStorageEvents()
    {
        string source = await ReadRepoFileAsync("src", "Unlimotion", "ServerStorage.cs");

        await Assert.That(source).Contains("_connection.Subscribe<ReceiveTaskItem>");
        await Assert.That(source).Contains("Type = UpdateType.Saved");
        await Assert.That(source).Contains("_connection.Subscribe<DeleteTaskItem>");
        await Assert.That(source).Contains("Type = UpdateType.Removed");
    }

    private static RouteAttribute? FindRoute<TRequest>(string expectedPath, string expectedVerb)
    {
        return typeof(TRequest)
            .GetCustomAttributes<RouteAttribute>()
            .SingleOrDefault(attribute =>
                string.Equals(attribute.Path, expectedPath, StringComparison.Ordinal) &&
                string.Equals(attribute.Verbs, expectedVerb, StringComparison.OrdinalIgnoreCase));
    }

    private static MethodInfo GetServiceMethod<TService, TRequest>(string name)
    {
        return typeof(TService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method =>
                method.Name == name &&
                method.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == typeof(TRequest));
    }

    private static string ExtractMethodSource(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (start < 0 || end < 0 || end <= start)
        {
            throw new InvalidOperationException($"Could not find method source between '{startMarker}' and '{endMarker}'.");
        }

        return source[start..end];
    }

    private static async Task<string> ReadRepoFileAsync(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return await File.ReadAllTextAsync(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(segments)}");
    }
}
