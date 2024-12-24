using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using ServiceStack;
using SignalR.EasyUse.Client;
using Splat;
using Unlimotion.TaskTree;
using Unlimotion.Interface;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds.Tasks;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Models;
using DynamicData;
using Unlimotion.Domain;

namespace Unlimotion;

public class ServerTaskStorage : ITaskStorage
{
    public event EventHandler<TaskStorageUpdateEventArgs>? Updating;
    //public event EventHandler<EventArgs> Initiated;
    public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }

    public ServerTaskStorage(string url)
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
        TaskTreeManager = Locator.Current.GetService<ITaskTreeManager>()
            ?? throw new NullReferenceException("ITaskTreeManager is not found");
    }

    ClientSettings settings;
    IConfiguration? configuration;
    IMapper? mapper;

    public event EventHandler OnSignOut;
    public event EventHandler OnSignIn;

    //TODO Проверить что не создаёт проблем при закрытии
    public bool IsActive = true;

    public ITaskTreeManager TaskTreeManager { get; set; }

    private Func<Exception, Task> connectionOnClosed()
    {
        return async (error) =>
        {
            while (IsActive)
            {
                await Task.Delay(new Random().Next(0, 5) * 1000);
                try
                {
                    bool connected = await Connect();
                    if (connected)
                        return;
                }
                catch (Exception e)
                {
                    //TODO показывать ошибку пользователю
                    //IsShowingLoginPage = true;
                    //User.ErrorMessageLoginPage.IsError = true;
                    //User.ErrorMessageLoginPage.GetErrorMessage(e.ToStatusCode().ToString());
                    //RegisterUser.ErrorMessageRegisterPage.IsError = true;
                    //RegisterUser.ErrorMessageRegisterPage.GetErrorMessage(e.ToStatusCode().ToString());
                }
            }
        };
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
                _connection.Closed -= connectionOnClosed();
                await _connection.StopAsync();
            }

            _connection = null;
            _hub = null;

            settings.AccessToken = null;
            settings.RefreshToken = null;
            configuration.Set("ClientSettings", settings);

            //WindowStates(WindowState.SignOut);
            OnSignOut?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
        }
        finally
        {
            //IsShowingLoginPage = true;
        }
    }

    public async Task Disconnect()
    {
        await SignOut();
    }
    public Task Init()
    {
        return Task.CompletedTask;
    }

    public async Task<TaskItem> Load(string itemId)
    {
        try
        {
            var task = await serviceClient.GetAsync(new GetTask() { Id=itemId });
            var mapped = mapper.Map<TaskItem>(task);
            return mapped;
        }
        catch (Exception e)
        {
            //TODO пробросить ошибку пользователю
        }
        return null;
    }

    public async Task<bool> Connect()
    {
        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(Url + "/ChatHub", (opts) =>
                    {
                        opts.HttpMessageHandlerFactory = (message) =>
                        {
                            if (message is HttpClientHandler clientHandler)
                                // always verify the SSL certificate
                                clientHandler.ServerCertificateCustomValidationCallback +=
                                    (sender, certificate, chain, sslPolicyErrors) => { return true; };
                            return message;
                        };
                    })
                .Build();

            _hub = _connection.CreateHub<IChatHub>();

            serviceClient.BearerToken = settings.AccessToken;

            _connection.Subscribe<LogOn>(async data =>
            {
                LogOn.LogOnStatus error = data.Error;
                switch (error)
                {
                    case LogOn.LogOnStatus.ErrorUserNotFound: //Пользователь не найден
                        {
                            await RegisterUser();
                            return;
                        }
                    case LogOn.LogOnStatus.ErrorExpiredToken: //Срок действия токена истек
                        {
                            await RefreshToken(settings, configuration);
                            break;
                        }
                    case LogOn.LogOnStatus.Ok: //Автовход по токену
                        {
                            settings.UserId = data.Id;
                            settings.Login = data.UserLogin;
                            settings.ExpireTime = data.ExpireTime;
                            //TODO получаем нужные данные 
                            IsSignedIn = true;
                            OnSignIn?.Invoke(this, EventArgs.Empty);
                            break;
                        }
                    default:
                        {
                            //TODO показывать ошибку пользователю
                            //User.ErrorMessageLoginPage.GetErrorMessage(((int)error).ToString());
                            break;
                        }
                }

            });

            _connection.Subscribe<ReceiveTaskItem>(async data =>
            {
                //обновление задачи в реалтайме
                Updating?.Invoke(this, new TaskStorageUpdateEventArgs
                {
                    Type = UpdateType.Saved,
                    Id = data.Id,
                });
            });

            _connection.Subscribe<DeleteTaskItem>(async data =>
            {
                //удаление задачи в реалтайме
                Updating?.Invoke(this, new TaskStorageUpdateEventArgs
                {
                    Type = UpdateType.Removed,
                    Id = data.Id,
                });
            });

            _connection.Closed += connectionOnClosed();
            await _connection.StartAsync();

            if (_connection.State == HubConnectionState.Connected)
            {
                if (!settings.RefreshToken.IsNullOrEmpty() && settings.ExpireTime < DateTimeOffset.Now)
                {
                    await RefreshToken(settings, configuration);
                }

                if (settings.AccessToken.IsNullOrEmpty())
                {
                    var storageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
                    try
                    {
                        var tokens = serviceClient.Post(new AuthViaPassword
                        { Login = storageSettings.Login, Password = storageSettings.Password });

                        settings.AccessToken = tokens.AccessToken;
                        settings.RefreshToken = tokens.RefreshToken;
                        settings.Login = storageSettings.Login;
                        serviceClient.BearerToken = tokens.AccessToken;
                        configuration.Set("ClientSettings", settings);
                    }
                    catch (Exception e)
                    {
                        await RegisterUser();
                        return _connection.State == HubConnectionState.Connected;
                    }
                }

                await Login();
                //IsShowingLoginPage = false;
                //IsShowingRegisterPage = false;
                //User.ErrorMessageLoginPage.ResetDisplayErrorMessage();
                IsConnected = _connection.State == HubConnectionState.Connected;
                //User.Password = "";
            }

            return _connection.State == HubConnectionState.Connected;
        }
        catch (Exception e)
        {
            //TODO показывать ошибку пользователю
            //User.ErrorMessageLoginPage.GetErrorMessage(e.ToStatusCode().ToString());
            //User.ErrorMessageLoginPage.IsError = true;
            //IsShowingLoginPage = _connection.State != HubConnectionState.Connected;
            return false;
        }
    }

    private async Task RegisterUser()
    {
        //Регистрируемся
        var request = new RegisterNewUser();
        try
        {
            var storageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
            var login = storageSettings.Login;
            var password = storageSettings.Password;
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
            configuration.Set("ClientSettings", settings);
            await Connect();
        }
        catch (Exception e)
        {
            //TODO показывать ошибку пользователю
            //Debug.WriteLine($"Ошибка регистрации {e.Message}");

            //RegisterUser.ErrorMessageRegisterPage.GetErrorMessage(e.ToStatusCode().ToString());
            //RegisterUser.ErrorMessageRegisterPage.IsError = true;
        }

        return;
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
        catch (Exception e)
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
            ipAddress = new WebClient().DownloadString("https://api.ipify.org");
        }
        catch (Exception e)
        {
            try
            {
                IPHostEntry ipHost = Dns.GetHostEntry("localhost");
                if (ipHost.AddressList.Length > 0)
                {
                    ipAddress = ipHost.AddressList.Last().ToString();
                }
            }
            catch (Exception exception)
            {
            }
        }

        var nameVersionClient = "Unlimotion Desktop Client 1.0";
        await _hub.Login(settings.AccessToken, operatingSystem, ipAddress, nameVersionClient);
    }

    public string Url { get; private set; }


    private HubConnection _connection;

    private readonly IJsonServiceClient serviceClient;
    private IChatHub _hub;

    public bool IsConnected { get; set; }

    public bool IsSignedIn { get; set; }

    public async IAsyncEnumerable<TaskItem> GetAll()
    {
        TaskItemPage? tasks = null;
        try
        {
            tasks = await serviceClient.GetAsync(new GetAllTasks());
        }
        catch (Exception e)
        {
            //TODO пробросить ошибку пользователю
        }

        if (tasks?.Tasks != null)
        {
            foreach (var task in tasks.Tasks)
            {
                var mapped = mapper.Map<TaskItem>(task);
                yield return mapped;
            }
        }
        //return taskItems;
    }

    public async Task<bool> Save(TaskItem item)
    {
        while (IsActive)
        {
            try
            {
                var hubTask = mapper.Map<TaskItemHubMold>(item);
                item.Id = await _hub.SaveTask(hubTask);
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
                await _hub.DeleteTasks(new List<string> { itemId });
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

    public async Task BulkInsert(IEnumerable<TaskItem> taskItems)
    {
        try
        {
            await serviceClient.PostAsync(new BulkInsertTasks { Tasks = taskItems.Select(i => mapper.Map<TaskItemMold>(i)).ToList() });
        }
        catch (Exception e)
        {
            //TODO пробросить ошибку пользователю
        }
    }

    /*protected virtual void OnInited()
    {
        Initiated?.Invoke(this, EventArgs.Empty);
    }*/
    public async Task<bool> Add(TaskItemViewModel change, TaskItemViewModel? currentTask = null, bool isBlocked = false)
    {
        var taskItemList = await TaskTreeManager.AddTask(
            change.Model,
            currentTask?.Model,
            isBlocked);
        foreach (var task in taskItemList)
        {
            UpdateCache(task);
            change.Id = task.Id;
        }
        return true;
    }

    public async Task<bool> AddChild(TaskItemViewModel change, TaskItemViewModel currentTask)
    {
        var taskItemList = await TaskTreeManager.AddChildTask(
            change.Model,
            currentTask.Model);
        foreach (var task in taskItemList)
        {
            UpdateCache(task);
        }
        change.Id = taskItemList.OrderByDescending(item => item.Id).First().Id;
        return true;
    }

    public async Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true)
    {
        var parentsItemList = await TaskTreeManager.DeleteTask(change.Model);
        foreach (var parent in parentsItemList)
        {
            UpdateCache(parent);
        }
        Tasks.Remove(change);

        return true;
    }

    public async Task<bool> Update(TaskItemViewModel change)
    {
        await TaskTreeManager.UpdateTask(change.Model);
        Tasks.AddOrUpdate(change);
        return true;
    }
    public async Task<bool> Update(TaskItem change)
    {
        await TaskTreeManager.UpdateTask(change);
        Tasks.AddOrUpdate(new TaskItemViewModel(change, this));
        return true;
    }

    public async Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents)
    {
        var additionalItemParents = new List<TaskItem>();
        foreach (var newParent in additionalParents)
        {
            additionalItemParents.Add(newParent.Model);
        }
        var taskItemList = await TaskTreeManager.CloneTask(
            change.Model,
            additionalItemParents);
        foreach (var task in taskItemList)
        {
            UpdateCache(task);
        }

        var clone = taskItemList.OrderByDescending(item => item.Id).First();
        change.Id = clone.Id;
        change.Parents.Add(clone.ParentTasks);

        return change;
    }

    public async Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents)
    {
        var additionalItemParents = new List<TaskItem>();
        foreach (var newParent in additionalParents)
        {
            additionalItemParents.Add(newParent.Model);
        }

        var taskItemList = await TaskTreeManager.AddNewParentToTask(
                change.Model,
                additionalParents[0].Model);

        foreach (var task in taskItemList)
        {
            UpdateCache(task);
        }
        return true;
    }
    private void UpdateCache(TaskItem task)
    {
        Tasks.AddOrUpdate(new TaskItemViewModel(task, this));
    }

    public async Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents, TaskItemViewModel? currentTask)
    {
        var taskItemList = await TaskTreeManager.MoveTaskToNewParent(
                change.Model,
                additionalParents?.FirstOrDefault()?.Model,
                currentTask?.Model);

        taskItemList.ForEach(UpdateCache);
        
        return true;
    }

    public async Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask)
    {
        var taskItemList = await TaskTreeManager.UnblockTask(
            taskToUnblock.Model,
            blockingTask.Model);

        taskItemList.ForEach(item => UpdateCache(item));

        return true;
    }

    public async Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask)
    {
        var taskItemList = await TaskTreeManager.BlockTask(
            change.Model,
            currentTask.Model);

        taskItemList.ForEach(item => UpdateCache(item));

        return true;
    }

    public async Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child)
    {
        var taskItemList = await TaskTreeManager.DeleteParentChildRelation(
            parent.Model,
            child.Model);

        taskItemList.ForEach(item => UpdateCache(item));
    }
}
