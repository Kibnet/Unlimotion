using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Funq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Raven.Client.Documents.Session;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Configuration;
using SignalR.EasyUse.Client;
using Unlimotion.Domain;
using Unlimotion.Interface;
using Unlimotion.Server;
using Unlimotion.Server.Hubs;

namespace Unlimotion.Test;

[NotInParallel("ServerStorageLiveIntegration")]
public sealed class ServerStorageLiveIntegrationTests
{
    [Test]
    public async Task ServerStorage_LiveSignalR_SaveTask_DeliversUpdateToSecondClientForSameUser()
    {
        await using var fixture = await ServerStorageLiveIntegrationFixture.StartAsync();
        string accessToken = await fixture.CreateAuthenticatedUserTokenAsync();
        string taskId = $"TaskItem/live-signalr-{Guid.NewGuid():N}";

        await using var sender = fixture.CreateHubConnection();
        await using var receiver = fixture.CreateHubConnection();

        var receivedTask = new TaskCompletionSource<ReceiveTaskItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        var senderEcho = new TaskCompletionSource<ReceiveTaskItem>(TaskCreationOptions.RunContinuationsAsynchronously);

        receiver.Subscribe<ReceiveTaskItem>(data => receivedTask.TrySetResult(data));
        sender.Subscribe<ReceiveTaskItem>(data => senderEcho.TrySetResult(data));

        await sender.StartAsync();
        await receiver.StartAsync();

        var senderHub = sender.CreateHub<IChatHub>();
        var receiverHub = receiver.CreateHub<IChatHub>();

        await senderHub.Login(accessToken, "test", "127.0.0.1", "live-integration-test");
        await receiverHub.Login(accessToken, "test", "127.0.0.1", "live-integration-test");

        string savedId = await senderHub.SaveTask(new TaskItemHubMold
        {
            Id = taskId,
            Title = "Live SignalR delivery",
            Description = "Задача отправлена live integration test",
            Status = Unlimotion.Domain.TaskStatus.NotReady,
            ContainsTasks = [],
            ParentTasks = [],
            BlocksTasks = [],
            BlockedByTasks = []
        });

        ReceiveTaskItem delivered = await WaitForAsync(receivedTask.Task, TimeSpan.FromSeconds(10));
        bool senderReceivedEcho = await CompletesWithinAsync(senderEcho.Task, TimeSpan.FromMilliseconds(500));

        await Assert.That(savedId).IsEqualTo(taskId);
        await Assert.That(delivered.Id).IsEqualTo(taskId);
        await Assert.That(delivered.Title).IsEqualTo("Live SignalR delivery");
        await Assert.That(senderReceivedEcho).IsFalse();
    }

    private static async Task<T> WaitForAsync<T>(Task<T> task, TimeSpan timeout)
    {
        var timeoutTask = Task.Delay(timeout);
        var completed = await Task.WhenAny(task, timeoutTask);
        if (completed == timeoutTask)
        {
            throw new TimeoutException($"Timed out after {timeout} waiting for live integration evidence.");
        }

        return await task;
    }

    private static async Task<bool> CompletesWithinAsync<T>(Task<T> task, TimeSpan timeout)
    {
        var timeoutTask = Task.Delay(timeout);
        return await Task.WhenAny(task, timeoutTask) == task;
    }

    private sealed class ServerStorageLiveIntegrationFixture : IAsyncDisposable
    {
        private readonly string _tempRoot;
        private readonly IHost _host;

        private ServerStorageLiveIntegrationFixture(string tempRoot, IHost host, string baseUrl)
        {
            _tempRoot = tempRoot;
            _host = host;
            BaseUrl = baseUrl;
        }

        public string BaseUrl { get; }

        public static async Task<ServerStorageLiveIntegrationFixture> StartAsync()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "Unlimotion.LiveIntegration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string serverProjectRoot = FindRepoFile("src", "Unlimotion.Server", "appsettings.json").Directory!.FullName;
            string contentRoot = Path.Combine(tempRoot, "ServerContentRoot");
            Directory.CreateDirectory(contentRoot);
            File.Copy(Path.Combine(serverProjectRoot, "appsettings.json"), Path.Combine(contentRoot, "appsettings.json"));
            File.Copy(
                Path.Combine(serverProjectRoot, "RavenDBLicense.json"),
                Path.Combine(tempRoot, "RavenDBLicense.json"));
            string url = $"http://127.0.0.1:{GetFreeTcpPort()}";

            var host = Host.CreateDefaultBuilder([])
                .UseEnvironment("Development")
                .ConfigureAppConfiguration((_, builder) =>
                {
                    builder.AddJsonFile(Path.Combine(contentRoot, "appsettings.json"), optional: false);
                    builder.AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string?>("RavenDb:DatabaseRecord:DatabaseName", $"UnlimotionLiveTest_{Guid.NewGuid():N}"),
                        new KeyValuePair<string, string?>("RavenDb:ServerOptions:ServerUrl", $"http://127.0.0.1:{GetFreeTcpPort()}"),
                        new KeyValuePair<string, string?>("RavenDb:ServerOptions:DataDirectory", Path.Combine(tempRoot, "RavenDB")),
                        new KeyValuePair<string, string?>("RavenDb:ServerOptions:LogsPath", Path.Combine(tempRoot, "Log", "RavenDB"))
                    });
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseContentRoot(contentRoot)
                        .UseUrls(url)
                        .UseStartup<LiveSignalRTestStartup>();
                })
                .Build();

            await host.StartAsync();
            return new ServerStorageLiveIntegrationFixture(tempRoot, host, url);
        }

        public async Task<string> CreateAuthenticatedUserTokenAsync()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string userId = $"User/{Guid.NewGuid()}";

            using (var scope = _host.Services.CreateScope())
            {
                var session = scope.ServiceProvider.GetRequiredService<IAsyncDocumentSession>();
                await session.StoreAsync(new User
                {
                    Id = userId,
                    Login = $"live-{suffix}",
                    DisplayName = $"Live {suffix}",
                    AboutMe = string.Empty,
                    RegisteredTime = DateTimeOffset.UtcNow
                });
                await session.SaveChangesAsync();
            }

            var jwtProvider = (JwtAuthProvider)AuthenticateService.GetAuthProvider("jwt");
            if (jwtProvider?.PublicKey == null)
            {
                throw new InvalidOperationException("JWT provider was not initialized for live SignalR test host.");
            }

            var payload = JwtAuthProvider.CreateJwtPayload(
                new AuthUserSession
                {
                    UserAuthId = userId,
                    CreatedAt = DateTime.UtcNow,
                    DisplayName = $"live-{suffix}"
                },
                issuer: jwtProvider.Issuer,
                expireIn: TimeSpan.FromHours(1));

            payload["useragent"] = "live-integration-test";
            payload["session"] = Guid.NewGuid().ToString();

            return JwtAuthProvider.CreateEncryptedJweToken(payload, jwtProvider.PublicKey.Value);
        }

        public HubConnection CreateHubConnection()
        {
            return new HubConnectionBuilder()
                .WithUrl($"{BaseUrl}/ChatHub")
                .Build();
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();

            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // RavenDB may keep files locked briefly after embedded server shutdown.
            }
        }

        private static FileInfo FindRepoFile(params string[] segments)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
                if (File.Exists(candidate))
                {
                    return new FileInfo(candidate);
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException($"Could not find repository file: {Path.Combine(segments)}");
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class LiveSignalRTestStartup
    {
        public LiveSignalRTestStartup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        private IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton(Configuration);
            services.AddSingleton<IAppSettings, Unlimotion.Server.AppSettings>();
            services.AddSingleton<LiveSignalRTestAppHost>();
            services.AddRavenDbServices();
            services.AddSignalR();
            services.AddSingleton<AutoMapper.IMapper>(Unlimotion.Server.AppModelMapping.ConfigureMapping());
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<ChatHub>("/chathub");
            });

            app.UseServiceStack(app.ApplicationServices.GetRequiredService<LiveSignalRTestAppHost>());
        }
    }

    private sealed class LiveSignalRTestAppHost : AppHostBase
    {
        private readonly IAppSettings _settings;

        public LiveSignalRTestAppHost(IAppSettings settings)
            : base("Unlimotion.LiveSignalRTest", typeof(LiveSignalRTestAppHost).Assembly)
        {
            _settings = settings;
        }

        public override void Configure(Container container)
        {
            var jwtProvider = new JwtAuthProvider(_settings)
            {
                RequireSecureConnection = false,
                HashAlgorithm = "RS512",
                PrivateKeyXml = _settings.GetString("Security:PrivateKeyXml"),
                EncryptPayload = true,
                ExpireTokensInDays = 1,
                ExpireRefreshTokensIn = TimeSpan.FromDays(30),
                AllowInQueryString = true,
                AllowInFormData = true,
                PopulateSessionFilter = (session, payload, _) =>
                {
                    if (payload.TryGetValue("session", out var sessionId) &&
                        session is AuthUserSession authUserSession)
                    {
                        authUserSession.Id = sessionId;
                    }
                }
            };
            jwtProvider.ServiceRoutes?.Clear();

            var authFeature = new AuthFeature(
                () => new AuthUserSession(),
                [jwtProvider])
            {
                IncludeAssignRoleServices = false,
                IncludeAuthMetadataProvider = false,
                IncludeRegistrationService = false
            };
            authFeature.ServiceRoutes.Clear();
            Plugins.Add(authFeature);
        }
    }

}
