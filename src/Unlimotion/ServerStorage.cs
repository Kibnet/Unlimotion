using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using ServiceStack;
using SignalR.EasyUse.Client;
using Unlimotion.Domain;
using Unlimotion.Interface;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds.Tasks;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion;

public class ServerStorage : IStorage, ITaskGraphDiagnosticStorage, ITaskClassificationCapabilityProvider
{
    public event EventHandler<TaskStorageUpdateEventArgs>? Updating;
    public event Action<Exception?>? OnConnectionError;
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event EventHandler? OnSignOut;
    public event EventHandler? OnSignIn
    {
        add { }
        remove { }
    }

    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private CancellationTokenSource? _connectCts;

    public string Url { get; private set; }
    public bool IsActive = true;
    public bool IsConnected { get; set; }
    public bool IsSignedIn { get; set; }
    public TaskStorageCapabilities RemoteTaskStorageCapabilities { get; private set; } =
        TaskStorageCapabilities.CreateUnsupported();
    public bool SupportsTaskClassification =>
        RemoteTaskStorageCapabilities.TaskClassificationSchemaVersion >=
        TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion;

    private HubConnection? _connection;
    private readonly IJsonServiceClient serviceClient;
    private readonly Func<GetAllTasks, Task<TaskItemPage>> fetchAllTasks;
    private readonly Func<TaskItem, Task<TaskItem>> saveTask;
    private readonly Func<string, Task<TaskItem?>> loadTask;
    private IChatHub? _hub;
    private ClientSettings settings = new();
    private IConfiguration? configuration;
    private readonly TaskSourceServerSettings? sourceServerSettings;
    private readonly Action<TaskSourceServerSettings>? persistServerSettings;
    private IMapper? mapper;

    public ServerStorage(string url, IConfiguration configuration)
        : this(url, configuration, sourceServerSettings: null, persistServerSettings: null)
    {
    }

    public ServerStorage(
        string url,
        IConfiguration configuration,
        TaskSourceServerSettings? sourceServerSettings,
        Action<TaskSourceServerSettings>? persistServerSettings)
    {
        Url = url;
        serviceClient = new JsonServiceClient(Url);
        fetchAllTasks = request => serviceClient.GetAsync(request);
        saveTask = SaveCoreAsync;
        loadTask = LoadCoreAsync;
        ServicePointManager.ServerCertificateValidationCallback +=
            (sender, cert, chain, sslPolicyErrors) => true;
        this.configuration = configuration;
        this.sourceServerSettings = sourceServerSettings;
        this.persistServerSettings = persistServerSettings;

        if (sourceServerSettings != null)
        {
            settings = TaskSourceSettingsAdapter.ToClientSettings(sourceServerSettings);
        }
        else
        {
            try
            {
                settings = configuration.Get<ClientSettings>("ClientSettings") ?? new ClientSettings();
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                settings = new ClientSettings();
            }
        }

        //Создание маппера
        mapper = AppModelMapping.ConfigureMapping();
    }

    internal ServerStorage(
        string url,
        IConfiguration configuration,
        Func<GetAllTasks, Task<TaskItemPage>> fetchAllTasks,
        Func<TaskItem, Task<TaskItem>>? saveTask = null,
        Func<string, Task<TaskItem?>>? loadTask = null)
        : this(url, configuration)
    {
        this.fetchAllTasks = fetchAllTasks ?? throw new ArgumentNullException(nameof(fetchAllTasks));
        this.saveTask = saveTask ?? this.saveTask;
        this.loadTask = loadTask ?? this.loadTask;
    }

    public async Task<bool> Connect()
    {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection != null &&
                (_connection.State == HubConnectionState.Connected || _connection.State == HubConnectionState.Connecting))
                return _connection.State == HubConnectionState.Connected;

            RemoteTaskStorageCapabilities = TaskStorageCapabilities.CreateUnsupported();

            if (_connection != null)
            {
                try 
                { 
                    await _connection.StopAsync().ConfigureAwait(false);
                    await _connection.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    //ничего не делаем
                }
                _connection = null;
                _hub = null;
            }

            _connectCts?.Cancel();
            _connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            _connection = new HubConnectionBuilder()
                .WithUrl(Url + "/ChatHub", opts =>
                {
                    opts.HttpMessageHandlerFactory = message =>
                    {
                        if (message is HttpClientHandler clientHandler)
                        {
                            clientHandler.ServerCertificateCustomValidationCallback +=
                                (sender, certificate, chain, sslPolicyErrors) => true;
                        }
                        return message;
                    };
                })
                .Build();

            RegisterHandlers();

            _hub = _connection.CreateHub<IChatHub>();

            _connection.Closed += ConnectionOnClosed;

            try
            {
                await _connection.StartAsync(_connectCts.Token).ConfigureAwait(false);
            }
            catch (Exception startEx)
            {
                OnConnectionError?.Invoke(startEx);
                return false;
            }

            RemoteTaskStorageCapabilities = await QueryTaskStorageCapabilitiesAsync(
                    () => _hub!.GetTaskStorageCapabilities())
                .ConfigureAwait(false);

            // После старта — проверяем/обновляем токен асинхронно
            try
            {
                serviceClient.BearerToken = settings.AccessToken;

                if (!string.IsNullOrEmpty(settings.RefreshToken) && settings.ExpireTime < DateTimeOffset.Now)
                {
                    await RefreshToken(settings).ConfigureAwait(false);
                }

                if (string.IsNullOrEmpty(settings.AccessToken))
                {
                    var credentials = GetCredentials();
                    try
                    {
                        var tokens = await serviceClient.PostAsync(new AuthViaPassword
                        {
                            Login = credentials.Login,
                            Password = credentials.Password
                        }).ConfigureAwait(false);

                        settings.AccessToken = tokens.AccessToken;
                        settings.RefreshToken = tokens.RefreshToken;
                        settings.Login = credentials.Login;
                        serviceClient.BearerToken = tokens.AccessToken;
                        PersistSettings();
                    }
                    catch (Exception authEx)
                    {
                        OnConnectionError?.Invoke(authEx);
                        await RegisterUser().ConfigureAwait(false);
                        return _connection.State == HubConnectionState.Connected;
                    }
                }

                await Login().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnConnectionError?.Invoke(ex);
            }

            IsConnected = _connection.State == HubConnectionState.Connected;
            if (IsConnected)
                OnConnected?.Invoke();

            return IsConnected;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task Disconnect()
    {
        await CloseConnectionAsync(clearCredentials: false, notifySignOut: false).ConfigureAwait(false);
    }

    public async Task SignOut()
    {
        await CloseConnectionAsync(clearCredentials: true, notifySignOut: true).ConfigureAwait(false);
    }

    private async Task CloseConnectionAsync(bool clearCredentials, bool notifySignOut)
    {
        try
        {
            //TODO очистить данные
            IsActive = false;
            IsSignedIn = false;
            IsConnected = false;
            RemoteTaskStorageCapabilities = TaskStorageCapabilities.CreateUnsupported();
            serviceClient.BearerToken = null;
            if (_connection != null)
            {
                _connection.Closed -= ConnectionOnClosed;
                try
                {
                    await _connection.StopAsync().ConfigureAwait(false);
                    await _connection.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    //ничего не делаем
                }
            }

            _connection = null;
            _hub = null;

            if (clearCredentials)
            {
                settings.AccessToken = string.Empty;
                settings.RefreshToken = string.Empty;
                PersistSettings();
            }

            //WindowStates(WindowState.SignOut);
            if (notifySignOut)
            {
                OnSignOut?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                OnDisconnected?.Invoke();
            }
        }
        catch (Exception)
        {
            // Exception during sign out, continuing cleanup
        }
    }

    public Task<TaskItem> Save(TaskItem item) => saveTask(item);

    private async Task<TaskItem> SaveCoreAsync(TaskItem item)
    {
        while (IsActive)
        {
            try
            {
                var hubTask = CreateOutboundTaskMold(
                    mapper,
                    item,
                    RemoteTaskStorageCapabilities);
                if (hubTask != null)
                {
                    item.Id = await _hub!.SaveTask(hubTask);
                }
                return item;
            }
            catch (Exception e)
            {
                //await Task.Delay(new Random().Next(0, 5) * 100);
                //TODO пробросить ошибку пользователю
                throw new Exception(e.Message);
            }
        }

        return null!;
    }

    internal static TaskItemHubMold? CreateOutboundTaskMold(
        IMapper? taskMapper,
        TaskItem item,
        TaskStorageCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(capabilities);

        var hubTask = taskMapper?.Map<TaskItemHubMold>(item);
        if (hubTask is null)
        {
            return null;
        }

        if (capabilities.TaskClassificationSchemaVersion <
            TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion)
        {
            // Older servers do not implement the presence/version contract. Keep
            // classification out of the outbound update entirely so an ordinary
            // task save cannot be mistaken for a classification write.
            hubTask.TaskClassificationSchemaVersion = null;
            hubTask.IsGoal = null;
            hubTask.AreaIds = null;
        }

        return hubTask;
    }

    public async Task<bool> Remove(string itemId)
    {
        while (IsActive)
        {
            try
            {
                await _hub!.DeleteTasks(new List<string> { itemId });
                return true;
            }
            catch (Exception e)
            {
                //await Task.Delay(new Random().Next(0, 5) * 100);
                //TODO пробросить ошибку пользователю
                throw new Exception(e.Message);
            }
        }

        return false;
    }

    public Task<TaskItem?> Load(string itemId) => loadTask(itemId);

    private async Task<TaskItem?> LoadCoreAsync(string itemId)
    {
        try
        {
            var task = await serviceClient.GetAsync(new GetTask { Id = itemId });
            var mapped = mapper?.Map<TaskItem>(task);
            return mapped;
        }
        catch (Exception)
        {
            //TODO пробросить ошибку пользователю
        }
        return null;
    }

    public async IAsyncEnumerable<TaskItem> GetAll()
    {
        TaskItemPage? tasks = null;
        try
        {
            tasks = await fetchAllTasks(new GetAllTasks());
        }
        catch (Exception)
        {
            // Failed to fetch external IP, using placeholder
            //TODO пробросить ошибку пользователю
        }

        if (tasks?.Tasks != null)
        {
            foreach (var task in tasks.Tasks)
            {
                var mapped = mapper?.Map<TaskItem>(task);
                if (mapped != null)
                    yield return mapped;
            }
        }
    }

    public async Task<TaskGraphReadResult> ReadGraphAsync()
    {
        var page = await fetchAllTasks(new GetAllTasks()).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server GetAllTasks returned no response.");
        var molds = page.Tasks
            ?? throw new InvalidOperationException("Server GetAllTasks response did not contain a task collection.");
        var tasks = new List<TaskItem>(molds.Count);
        var loadErrors = new List<TaskGraphLoadError>();
        for (var index = 0; index < molds.Count; index++)
        {
            var source = $"<server:GetAllTasks:{index}>";
            var mold = molds[index];
            if (mold is null)
            {
                loadErrors.Add(new TaskGraphLoadError(
                    source,
                    "Server GetAllTasks response contained a null task."));
                continue;
            }

            try
            {
                var task = mapper?.Map<TaskItem>(mold);
                if (task is null)
                {
                    loadErrors.Add(new TaskGraphLoadError(
                        source,
                        "Server GetAllTasks task could not be mapped."));
                    continue;
                }

                tasks.Add(task);
            }
            catch (Exception exception)
            {
                loadErrors.Add(new TaskGraphLoadError(source, exception.Message));
            }
        }

        var filesByTaskId = tasks
            .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
            .GroupBy(static task => task.Id, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static _ => "<server:GetAllTasks>",
                StringComparer.Ordinal);
        var duplicateIdIssues = tasks
            .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
            .Select(static (task, index) => (task.Id, Index: index))
            .GroupBy(static item => item.Id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => new TaskGraphDuplicateIdIssue(
                group.Key,
                group.Select(static item => $"<server:GetAllTasks:{item.Index}>").ToArray()))
            .ToArray();

        return new TaskGraphReadResult(
            tasks,
            filesByTaskId,
            loadErrors,
            duplicateIdIssues);
    }

    public async Task BulkInsert(IEnumerable<TaskItem> taskItems)
    {
        try
        {
            await serviceClient.PostAsync(new BulkInsertTasks { Tasks = taskItems.Select(i => mapper?.Map<TaskItemMold>(i)!).ToList() });
        }
        catch (Exception)
        {
            //TODO пробросить ошибку пользователю
        }
    }

    private async Task ConnectionOnClosed(Exception? exception)
    {
        RemoteTaskStorageCapabilities = TaskStorageCapabilities.CreateUnsupported();
        OnConnectionError?.Invoke(exception);

        var rnd = new Random();
        while (IsActive)
        {
            await Task.Delay(TimeSpan.FromSeconds(rnd.Next(2, 6))).ConfigureAwait(false);
            try
            {
                var ok = await Connect().ConfigureAwait(false); // защищено семафором
                if (ok)
                {
                    OnConnected?.Invoke();
                    return;
                }
            }
            catch (Exception ex)
            {
                OnConnectionError?.Invoke(ex);
            }
        }

        IsConnected = false;
        OnDisconnected?.Invoke();
    }

    private void RegisterHandlers()
    {
        _connection!.Subscribe<LogOn>(async data =>
        {
            try
            {
                switch (data.Error)
                {
                    case LogOn.LogOnStatus.ErrorUserNotFound:
                        await RegisterUser().ConfigureAwait(false);
                        return;
                    case LogOn.LogOnStatus.ErrorExpiredToken:
                        await RefreshToken(settings).ConfigureAwait(false);
                        break;
                    case LogOn.LogOnStatus.Ok:
                        settings.UserId = data.Id;
                        settings.Login = data.UserLogin;
                        settings.ExpireTime = data.ExpireTime;
                        PersistSettings();

                        OnConnected?.Invoke();
                        break;
                }
            }
            catch (Exception ex)
            {
                OnConnectionError?.Invoke(ex);
            }
        });

        _connection.Subscribe<ReceiveTaskItem>(async data =>
        {
            try
            {
                var taskItem = mapper?.Map<TaskItem>(data);

                if (taskItem != null) 
                {
                    OnUpdating(new TaskStorageUpdateEventArgs
                    {
                        Id = taskItem.Id!,
                        Type = UpdateType.Saved
                    });
                }
            }
            catch (Exception ex) { OnConnectionError?.Invoke(ex); }
        });

        _connection.Subscribe<DeleteTaskItem>(async data =>
        {
            try
            {
                OnUpdating(new TaskStorageUpdateEventArgs
                {
                    Id = data.Id,
                    Type = UpdateType.Removed
                });
            }
            catch (Exception ex) { OnConnectionError?.Invoke(ex); }
        });
    }

    private async Task RegisterUser()
    {
        //Регистрируемся
        var request = new RegisterNewUser();
        try
        {
            var credentials = GetCredentials();
            var login = credentials.Login;
            var password = credentials.Password;
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                //TODO показать ошибку пользователю
                //RegisterUser.ErrorMessageRegisterPage.GetErrorMessage("Не заполнены Логин и/или Пароль");
                //RegisterUser.ErrorMessageRegisterPage.IsError = true;
                return;
            }

            request.Login = login;
            request.Password = password;
            request.UserName = login;

            var tokenResult = await serviceClient.PostAsync(request);

            settings.AccessToken = tokenResult.AccessToken;
            settings.RefreshToken = tokenResult.RefreshToken;
            settings.Login = login;
            PersistSettings();
            await Connect();
        }
        catch (Exception)
        {
            //TODO показывать ошибку пользователю
            //Debug.WriteLine($"Ошибка регистрации {e.Message}");

            //RegisterUser.ErrorMessageRegisterPage.GetErrorMessage(e.ToStatusCode().ToString());
            //RegisterUser.ErrorMessageRegisterPage.IsError = true;
        }
    }

    private async Task RefreshToken(ClientSettings settings)
    {
        serviceClient.BearerToken = settings.RefreshToken;
        try
        {
            var tokenResult = await serviceClient.PostAsync(new PostRefreshToken());
            settings.AccessToken = tokenResult.AccessToken;
            settings.RefreshToken = tokenResult.RefreshToken;
            settings.ExpireTime = tokenResult.ExpireTime;
            serviceClient.BearerToken = settings.AccessToken;
            PersistSettings();
            await Login();
            IsSignedIn = true;
        }
        catch (Exception)
        {
            //TODO вывести ошибку пользователю
            //User.ErrorMessageLoginPage.GetErrorMessage("419");
            await SignOut();
        }
    }

    private async Task Login()
    {
        var bits = Environment.Is64BitOperatingSystem ? "PC 64bit, " : "PC 32bit, ";
        var operatingSystem = bits + RuntimeInformation.OSDescription;

        string ipAddress = "";
        try
        {
            using var httpClient = new HttpClient();
            ipAddress = await httpClient.GetStringAsync("https://api.ipify.org").ConfigureAwait(false);
        }
        catch (Exception)
        {
            try
            {
                IPHostEntry ipHost = Dns.GetHostEntry("localhost");
                if (ipHost.AddressList.Length > 0)
                {
                    ipAddress = ipHost.AddressList.Last().ToString();
                }
            }
            catch (Exception)
            {
                // Use empty string if all attempts fail
            }
        }

        var nameVersionClient = "Unlimotion Desktop Client 1.0";
        await _hub!.Login(settings.AccessToken, operatingSystem, ipAddress, nameVersionClient);
    }

    internal static async Task<TaskStorageCapabilities> QueryTaskStorageCapabilitiesAsync(
        Func<Task<TaskStorageCapabilities>> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            return await query().ConfigureAwait(false)
                ?? TaskStorageCapabilities.CreateUnsupported();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Task storage capabilities are unavailable: {exception.Message}");
            return TaskStorageCapabilities.CreateUnsupported();
        }
    }

    protected virtual void OnUpdating(TaskStorageUpdateEventArgs e)
    {
        Updating?.Invoke(this, e);
    }

    private (string Login, string Password) GetCredentials()
    {
        if (sourceServerSettings != null)
        {
            return (sourceServerSettings.Login, sourceServerSettings.Password);
        }

        var storageSettings = configuration?.Get<TaskStorageSettings>("TaskStorage");
        return (storageSettings?.Login ?? string.Empty, storageSettings?.Password ?? string.Empty);
    }

    private void PersistSettings()
    {
        if (sourceServerSettings != null)
        {
            TaskSourceSettingsAdapter.CopyFromClientSettings(settings, sourceServerSettings);
            persistServerSettings?.Invoke(sourceServerSettings);
            return;
        }

        configuration?.Set("ClientSettings", settings);
    }
}
