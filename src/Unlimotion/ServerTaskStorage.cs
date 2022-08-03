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
using Unlimotion.Interface;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class ServerTaskStorage : ITaskStorage
{
    public ServerTaskStorage(string url)
    {
        Url = url;
        serviceClient = new JsonServiceClient(Url);
        ServicePointManager.ServerCertificateValidationCallback +=
            (sender, cert, chain, sslPolicyErrors) => true;
        configuration = Locator.Current.GetService<IConfiguration>();
        settings = configuration.Get<ClientSettings>("ClientSettings");
        if (settings == null)
        {
            settings = new ClientSettings();
        }
        mapper = Locator.Current.GetService<IMapper>();
    }

    ClientSettings settings;
    IConfiguration? configuration;
    IMapper? mapper;

    public event EventHandler OnSignOut;
    public event EventHandler OnSignIn;

    private Func<Exception, Task> connectionOnClosed()
    {
        return async (error) =>
        {
            await Task.Delay(new Random().Next(0, 5) * 1000);
            try
            {
                await _connection.StartAsync();
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
        };
    }

    public async void SignOut()
    {
        try
        {
            //TODO очистить данные

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


    public async Task Connect()
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

            ///Обновление отредактированных сообщений в окне чата.
            _connection.Subscribe<ReceiveTaskItem>(async data =>
            {
                //TODO обновление задачи в реалтайме
                //if (messageDictionary.TryGetValue(data.Id, out var keyValue))
                //{
                //    keyValue.Text = data.Text;
                //    keyValue.LastEditTime = data.LastEditTime;

                //    if (data.QuotedMessage != null)
                //    {
                //        MessageViewModel newQuotedMessage = mapper.Map<MessageViewModel>(data.QuotedMessage);
                //        if (messageDictionary.TryGetValue(data.QuotedMessage.Id, out var message))
                //        {
                //            mapper.Map(newQuotedMessage, message);
                //            keyValue.QuotedMessage = message;
                //        }
                //        else
                //        {
                //            newQuotedMessage.IsMyMessage = User.Id == data.QuotedMessage.UserId;
                //            newQuotedMessage.Attachments = data.QuotedMessage.Attachments?
                //                                                    .Select(s =>
                //                                                    {
                //                                                        var attah = mapper?.Map<AttachmentMold>(s);
                //                                                        var newAttachment = new AttachmentMessageViewModel(attah);
                //                                                        return newAttachment;
                //                                                    }).ToList();

                //            messageDictionary[data.QuotedMessage.Id] = newQuotedMessage;
                //            keyValue.QuotedMessage = newQuotedMessage;
                //        }
                //    }
                //    else
                //    {
                //        keyValue.QuotedMessage = null;
                //    }
                //}
            });

            ///Получает новые сообщения и добавляет их в окно чата. 
            _connection.Subscribe<DeleteTaskItem>(async data =>
            {
                //TODO удаление задачи в реалтайме
                //MessageViewModel newMessage = mapper.Map<MessageViewModel>(data);

                //newMessage.IsMyMessage = User.Id == data.UserId;
                //newMessage.Attachments = data.Attachments?
                //    .Select(s =>
                //    {
                //        var attah = mapper?.Map<AttachmentMold>(s);
                //        var newAttachment = new AttachmentMessageViewModel(attah);
                //        return newAttachment;
                //    }).ToList();
                //if (data.QuotedMessage != null)
                //{
                //    MessageViewModel newQuotedMessage = mapper.Map<MessageViewModel>(data.QuotedMessage);
                //    if (messageDictionary.TryGetValue(data.QuotedMessage.Id, out var quotedMessage))
                //    {
                //        mapper.Map(newQuotedMessage, quotedMessage);
                //        newMessage.QuotedMessage = quotedMessage;
                //    }
                //    else
                //    {
                //        newQuotedMessage.IsMyMessage = User.Id == data.QuotedMessage.UserId;
                //        newQuotedMessage.Attachments = data.QuotedMessage.Attachments?
                //            .Select(s =>
                //            {
                //                var attah = mapper?.Map<AttachmentMold>(s);
                //                var newAttachment = new AttachmentMessageViewModel(attah);
                //                return newAttachment;
                //            }).ToList();

                //        messageDictionary[newQuotedMessage.Id] = newQuotedMessage;
                //    }
                //}
                //if (Messages.Count != 0)
                //{
                //    if (Messages.Last().UserId != data.UserId)
                //    {
                //        newMessage.ShowNickname = true;
                //    }
                //}
                //else
                //{
                //    newMessage.ShowNickname = true;
                //}
                //Messages.Add(newMessage);
                //MessageReceived?.Invoke(new ReceivedMessageArgs(newMessage));
                //messageDictionary[newMessage.Id] = newMessage;
                //AutoScrollWhenSendingMyMessage = User.Id == newMessage.UserId;
            });

            _connection.Closed += connectionOnClosed();
            await _connection.StartAsync();

            if (!settings.RefreshToken.IsNullOrEmpty() && settings.ExpireTime < DateTimeOffset.Now)
            {
                await RefreshToken(settings, configuration);
            }

            if (settings.AccessToken.IsNullOrEmpty())
            {
                    var storageSettings = configuration.Get<TaskStorageSettings>();
                try
                {
                    var tokens = serviceClient.Post(new AuthViaPassword
                            { Login = storageSettings.Login, Password = storageSettings.Password });

                    settings.AccessToken = tokens.AccessToken;
                    settings.RefreshToken = tokens.RefreshToken;
                        settings.Login = storageSettings.Login;
                        configuration.Set("ClientSettings", settings);
                }
                catch (Exception e)
                {
                    await RegisterUser();
                    return;
                }
            }

            await Login();
            //IsShowingLoginPage = false;
            //IsShowingRegisterPage = false;
            //User.ErrorMessageLoginPage.ResetDisplayErrorMessage();
            IsConnected = _connection.State == HubConnectionState.Connected;
            //User.Password = "";
        }
        catch (Exception e)
        {
            //TODO показывать ошибку пользователю
            //User.ErrorMessageLoginPage.GetErrorMessage(e.ToStatusCode().ToString());
            //User.ErrorMessageLoginPage.IsError = true;
            //IsShowingLoginPage = _connection.State != HubConnectionState.Connected;
        }
    }

    private async Task RegisterUser()
    {
        //Регистрируемся
        var request = new RegisterNewUser();
        try
        {
            var storageSettings = configuration.Get<TaskStorageSettings>();
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
            configuration.Set("ClientSettings",settings);
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
            configuration.Set("ClientSettings", configuration);
            await Login();
            IsSignedIn = true;
        }
        catch (Exception e)
        {
            //TODO вывести ошибку пользователю
            //User.ErrorMessageLoginPage.GetErrorMessage("419");
            SignOut();
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

    public IEnumerable<TaskItem> GetAll()
    {
        TaskItemPage? tasks = null;
        try
        {
            tasks = serviceClient.GetAsync(new GetAllTasks()).Result;
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
    }

    public async Task<bool> Save(TaskItem item)
    {
        try
        {
            var hubTask = mapper.Map<TaskItemHubMold>(item);
            item.Id = await _hub.SaveTask(hubTask);
        }
        catch (Exception e)
        {
            //TODO пробросить ошибку пользователю
        }
        return true;
    }

    public async Task<bool> Remove(string itemId)
    {
        try
        {
            await _hub.DeleteTasks(new List<string> { itemId });
        }
        catch (Exception e)
        {
            //TODO пробросить ошибку пользователю
        }
        return true;
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
}