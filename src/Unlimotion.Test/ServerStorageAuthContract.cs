using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ServiceStack;
using Unlimotion.Server.ServiceInterface;
using Unlimotion.Server.ServiceModel;

namespace Unlimotion.Test;

internal static class ServerStorageAuthContract
{
    public static async Task<ServerStorageAuthScenarioResult> ExecuteLoginRegisterRefreshScenarioAsync()
    {
        var result = new ServerStorageAuthScenarioResult();

        await AssertLoginRegisterRefreshFlowExposesExpectedAuthContractsAsync();
        result.AuthRoutesExposed = true;
        result.LoginDefaultsVerified = true;
        result.RegisterDefaultsVerified = true;

        await AssertRefreshTokenRequiresAuthenticatedRefreshRequestAsync();
        result.RefreshTokenAuthenticatedRequestRequired = true;

        await AssertConnectUsesLoginRegisterAndRefreshTokenFlowAsync();
        result.ClientConnectUsesAuthFlow = true;
        result.RefreshTokenPersistenceVerified = true;

        return result;
    }

    public static async Task AssertLoginRegisterRefreshFlowExposesExpectedAuthContractsAsync()
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

    public static async Task AssertRefreshTokenRequiresAuthenticatedRefreshRequestAsync()
    {
        MethodInfo refresh = GetServiceMethod<AuthService, PostRefreshToken>("Post");

        await Assert.That(refresh.GetCustomAttribute<AuthenticateAttribute>()).IsNotNull();
    }

    public static async Task AssertConnectUsesLoginRegisterAndRefreshTokenFlowAsync()
    {
        string source = await ReadRepoFileAsync("src/Unlimotion/ServerStorage.cs");

        await Assert.That(source).Contains("serviceClient.PostAsync(new AuthViaPassword");
        await Assert.That(source).Contains("await RefreshToken(settings, configuration!)");
        await Assert.That(source).Contains("await RegisterUser().ConfigureAwait(false)");
        await Assert.That(source).Contains("settings.RefreshToken = tokens.RefreshToken");
        await Assert.That(source).Contains("settings.RefreshToken = tokenResult.RefreshToken");
    }

    public static async Task AssertLoginRegisterRefreshScenarioResultAsync(
        ServerStorageAuthScenarioResult result)
    {
        await Assert.That(result.AuthRoutesExposed).IsTrue();
        await Assert.That(result.LoginDefaultsVerified).IsTrue();
        await Assert.That(result.RegisterDefaultsVerified).IsTrue();
        await Assert.That(result.RefreshTokenAuthenticatedRequestRequired).IsTrue();
        await Assert.That(result.ClientConnectUsesAuthFlow).IsTrue();
        await Assert.That(result.RefreshTokenPersistenceVerified).IsTrue();
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

    private static Task<string> ReadRepoFileAsync(string relativePath)
    {
        return File.ReadAllTextAsync(PlatformShellProjectContracts.GetRepositoryPath(relativePath));
    }
}

internal sealed class ServerStorageAuthScenarioResult
{
    public bool AuthRoutesExposed { get; set; }

    public bool LoginDefaultsVerified { get; set; }

    public bool RegisterDefaultsVerified { get; set; }

    public bool RefreshTokenAuthenticatedRequestRequired { get; set; }

    public bool ClientConnectUsesAuthFlow { get; set; }

    public bool RefreshTokenPersistenceVerified { get; set; }
}
