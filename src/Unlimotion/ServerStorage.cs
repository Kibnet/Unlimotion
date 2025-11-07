using System;
using System.Collections.Generic;
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
using Splat;
using Unlimotion.Domain;
using Unlimotion.Interface;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds.Tasks;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class ServerStorage : IStorage
{
    public event EventHandler<TaskStorageUpdateEventArgs>? Updating;
    public event Action<Exception?>? OnConnectionError;
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event EventHandler? OnSignOut;
    public event EventHandler? OnSignIn;

    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private CancellationTokenSource? _connectCts;

    public string Url { get; private set; }
    public bool IsActive = true;
    public bool IsConnected { get; set; }
    public bool IsSignedIn { get; set; }

    private HubConnection? _connection;
    private readonly IJsonServiceClient serviceClient;
    private IChatHub? _hub;
    private ClientSettings settings;
    private IConfiguration? configuration;
    private IMapper? mapper;

    public ServerStorage(string url)
    {
        Url = url;
        serviceClient = new JsonServiceClient(Url);
        ServicePointManager.ServerCertificateValidationCallback +=
            (sender, cert, chain, sslPolicyErrors) => true;
        configuration = Locator.Current.GetService<IConfiguration>();

        try
        {
            settings = configuration.Get<ClientSettings>("ClientSettings");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        if (settings == null)
        {
            settings = new ClientSettings();
        }
        mapper = Locator.Current.GetService<IMapper>();
    }

    public async Task<bool> Connect()
    {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection != null &&
                (_connection.State == HubConnectionState.Connected || _connection.State == HubConnectionState.Connecting))
                return _connection.State == HubConnectionState.Connected;

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

            // После старта — проверяем/обновляем токен асинхронно
            try
            {
                serviceClient.BearerToken = settings.AccessToken;

                if (!string.IsNullOrEmpty(settings.RefreshToken) && settings.ExpireTime < DateTimeOffset.Now)
                {
                    await RefreshToken(settings, configuration!).ConfigureAwait(false);
                }

                if (string.IsNullOrEmpty(settings.AccessToken))
                {
                    var storageSettings = configuration?.Get<TaskStorageSettings>("TaskStorage");
                    try
                    {
                        var tokens = await serviceClient.PostAsync(new AuthViaPassword
                        {
                            Login = storageSettings?.Login ?? string.Empty,
                            Password = storageSettings?.Password ?? string.Empty
                        }).ConfigureAwait(false);

                        settings.AccessToken = tokens.AccessToken;
                        settings.RefreshToken = tokens.RefreshToken;
                        settings.Login = storageSettings?.Login;
                        serviceClient.BearerToken = tokens.AccessToken;
                        if (configuration != null)
                        {
                            configuration.Set("ClientSettings", settings);
                        }
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
        await SignOut();
    }

    public async Task SignOut()
    {
        try
        {
            //TODO очистить данные
            IsActive = false;
            IsSignedIn = false;
            IsConnected = false;
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

            settings.AccessToken = null;
            settings.RefreshToken = null;
            if (configuration != null)
            {
                configuration.Set("ClientSettings", settings);
            }

            //WindowStates(WindowState.SignOut);
            OnSignOut?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Exception during sign out, continuing cleanup
        }
    }

    public async Task<bool> Save(TaskItem item)
    {
        while (IsActive)
        {
            try
            {
                var hubTask = mapper?.Map<TaskItemHubMold>(item);
                if (hubTask != null)
                {
                    item.Id = await _hub!.SaveTask(hubTask);
                }
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

    public async Task<TaskItem?> Load(string itemId)
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
            //var task = serviceClient.Get(new GetAllTasks());;
            tasks = await serviceClient.GetAsync(new GetAllTasks());
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
                        await RefreshToken(settings, configuration!).ConfigureAwait(false);
                        break;
                    case LogOn.LogOnStatus.Ok:
                        settings.UserId = data.Id;
                        settings.Login = data.UserLogin;
                        settings.ExpireTime = data.ExpireTime;

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
            var storageSettings = configuration?.Get<TaskStorageSettings>("TaskStorage");
            var login = storageSettings?.Login;
            var password = storageSettings?.Password;
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
            if (configuration != null)
            {
                configuration.Set("ClientSettings", settings);
            }
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

    private async Task RefreshToken(ClientSettings settings, IConfiguration configuration)
    {
        serviceClient.BearerToken = settings.RefreshToken;
        try
        {
            var tokenResult = await serviceClient.PostAsync(new PostRefreshToken());
            settings.AccessToken = tokenResult.AccessToken;
            settings.RefreshToken = tokenResult.RefreshToken;
            settings.ExpireTime = tokenResult.ExpireTime;
            serviceClient.BearerToken = settings.AccessToken;
            configuration.Set("ClientSettings", settings);
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

    protected virtual void OnUpdating(TaskStorageUpdateEventArgs e)
    {
        Updating?.Invoke(this, e);
    }
}
