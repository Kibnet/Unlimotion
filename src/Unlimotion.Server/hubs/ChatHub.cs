using AutoMapper;
using Microsoft.AspNetCore.SignalR;
using Raven.Client.Documents.Session;
using Serilog;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Host;
using SignalR.EasyUse.Server;
using Unlimotion.Interface;
using Unlimotion.Server.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Unlimotion.Server.Hubs
{
    public class ChatHub : Hub, IChatHub
    {
       
        public ChatHub(IAsyncDocumentSession ravenSession, IMapper mapper)
        {
            _ravenSession = ravenSession;
            Mapper = mapper;
        }
        private IMapper Mapper;
        private string _loginedGroup = "Logined";

        private readonly IAsyncDocumentSession _ravenSession;

        public async Task UpdateMyDisplayName(string userDispalyName)
        {
            if (Context.Items["nickname"] as string != userDispalyName)
            {
                await Clients.Group(_loginedGroup).SendAsync(new UpdateUserDisplayName
                {
                    Id = Context.Items["uid"] as string,
                    DisplayName = userDispalyName,
                    UserLogin = Context.Items["login"] as string
                });

                Context.Items["nickname"] = userDispalyName;
                Log.Information(
                    $"User Id:{Context.Items["uid"] as string} change display user name to {userDispalyName}");
            }
        }

        public async Task SaveTask(TaskItemHubMold hubTask)
        {
            try
            {
                string uid = Context.Items["uid"].ToString();
                var task = await _ravenSession.LoadAsync<TaskItem>(hubTask.Id);
                if (task == null)
                {
                    var taskItem = Mapper.Map<TaskItem>(hubTask);
                    taskItem.CreatedDateTime = DateTimeOffset.UtcNow;
                    taskItem.UserId = uid;

                    await _ravenSession.StoreAsync(taskItem);
                    await _ravenSession.SaveChangesAsync();

                    var receiveTask = Mapper.Map<ReceiveTaskItem>(taskItem);

                    await Clients.Users(uid).SendAsync(receiveTask);

                    var logMessage = $"User {Context.Items["nickname"]}({Context.Items["login"]}) save task {taskItem.Id}";
                    Log.Information(logMessage);
                }
                else if (task.UserId == uid)
                {
                    var taskItem = Mapper.Map(hubTask, task);
                    
                    await _ravenSession.SaveChangesAsync();

                    var receiveTask = Mapper.Map<ReceiveTaskItem>(taskItem);

                    await Clients.Users(uid).SendAsync(receiveTask);

                    Log.Information($"User {Context.Items["nickname"]}({Context.Items["login"]}) update task {taskItem.Id}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        
        /// <summary>
        /// Удаление задач (для пользователя)
        /// </summary>
        /// <param name="idTasks"></param>
        /// <returns></returns>
        public async Task DeleteTasks(List<string> idTasks)
        {
            try
            {
                string uid = Context.Items["uid"].ToString();

                //Получение своих задач для удаления
                var tasks = await _ravenSession.LoadAsync<TaskItem>(idTasks);
                var listMessages = tasks.Values.Where(item => item.UserId == uid).ToList();

                //Удаление из БД
                foreach (var item in listMessages)
                {
                    _ravenSession.Delete(item);
                }
                await _ravenSession.SaveChangesAsync();

                //Отправка сообщения об удалении задачи
                foreach (var item in listMessages)
                {
                    var deleteTask = new DeleteTaskItem
                    {
                        Id = item.Id,
                        UserId = uid
                    };
                    await Clients.Users(uid).SendAsync(deleteTask);
                }
            }
            catch
            {

            }

        }

        public async Task Login(string token, string operatingSystem, string ipAddress, string nameVersionClient)
        {
            var jwtAuthProviderReader = (JwtAuthProviderReader)AuthenticateService.GetAuthProvider("jwt");

            try
            {
                var jwtPayload = jwtAuthProviderReader.GetVerifiedJwtPayload(new BasicHttpRequest(), token.Split('.'));
                await Groups.AddToGroupAsync(this.Context.ConnectionId, _loginedGroup);
                Context.Items["login"] = jwtPayload["name"];
                Context.Items["uid"] = jwtPayload["sub"];
                Context.Items["session"] = jwtPayload["session"];


                var logOn = new LogOn
                {
                    Id = jwtPayload["sub"],
                    UserLogin = jwtPayload["name"],
                    Error = LogOn.LogOnStatus.Ok,
                };
                var user = await _ravenSession.LoadAsync<User>(jwtPayload["sub"]);
                if (user != null)
                {
                    logOn.UserName = user.DisplayName;
                    Context.Items["nickname"] = user.DisplayName;
                }
                else
                {
                    //Если пользователя нет в БД
                    await Clients.Caller.SendAsync(new LogOn
                    {
                        Error = LogOn.LogOnStatus.ErrorUserNotFound
                    });
                    throw new TokenException("User not found");
                }

                if (long.TryParse(jwtPayload["exp"], out long expire))
                {
                    logOn.ExpireTime = DateTimeOffset.FromUnixTimeSeconds(expire);
                    if (logOn.ExpireTime < DateTimeOffset.UtcNow)
                    {
                        //Если время жизни токена закончилось
                        await Clients.Caller.SendAsync(new LogOn
                        {
                            Error = LogOn.LogOnStatus.ErrorExpiredToken
                        });
                        throw new TokenException("Token is expired");
                    }
                }

                await Clients.Caller.SendAsync(logOn);
                var userLoginAudit = await _ravenSession.LoadAsync<LoginAudit>(jwtPayload["sub"] + "/LoginAudit");
                if (userLoginAudit != null)
                {
                    if (jwtPayload["session"] != userLoginAudit.SessionId)
                    {
                        userLoginAudit.NameVersionClient = nameVersionClient;
                        userLoginAudit.OperatingSystem = operatingSystem;
                        userLoginAudit.IpAddress = ipAddress;
                        userLoginAudit.DateOfEntry = DateTime.Now;
                        userLoginAudit.SessionId = jwtPayload["session"];

                        await _ravenSession.StoreAsync(userLoginAudit);
                        await _ravenSession.SaveChangesAsync();
                    }
                }
                else
                {
                    userLoginAudit = new LoginAudit
                    {
                        Id = jwtPayload["sub"] + "/LoginAudit",
                        OperatingSystem = operatingSystem,
                        DateOfEntry = DateTime.Now,
                        IpAddress = ipAddress,
                        NameVersionClient = nameVersionClient,
                        SessionId = jwtPayload["session"]
                    };
                    await _ravenSession.StoreAsync(userLoginAudit);
                    await _ravenSession.SaveChangesAsync();
                }
                Log.Information($"Connected {Context.Items["login"]}({Context.Items["uid"]}) with session {Context.Items["session"]}");
            }
            catch (Exception e)
            {
                Log.Warning($"Bad token from connection {Context.ConnectionId}");
            }
        }
    }
}
