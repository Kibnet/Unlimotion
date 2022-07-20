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

        public async Task SendMessage(HubMessage hubMessage)
        {
            var messageItem = new Message
            {
                UserId = Context.Items["uid"] as string,
                Text = hubMessage.Message.Trim(),
                PostTime = DateTimeOffset.UtcNow,
                ChatId = hubMessage.ChatId,
                Attachments = hubMessage.Attachments?.Select(s => s.Id).ToList(),
                IdQuotedMessage = hubMessage.IdQuotedMessage
            };

            await _ravenSession.StoreAsync(messageItem);
            await _ravenSession.SaveChangesAsync();

            var quotedReceiveMessage = await GetQuotedReceiveMessage(hubMessage.IdQuotedMessage);

            await Clients.Group(_loginedGroup).SendAsync(new ReceiveMessage
            {
                Id = messageItem.Id,
                UserLogin = Context.Items["login"] as string,
                UserNickname = Context.Items["nickname"] as string,
                Text = hubMessage.Message,
                PostTime = messageItem.PostTime,
                ChatId = hubMessage.ChatId,
                UserId = messageItem.UserId,
                Attachments = hubMessage.Attachments,
                QuotedMessage= quotedReceiveMessage
            });

            var logMessage = hubMessage.IdQuotedMessage.IsNullOrEmpty() ? $"User {Context.Items["nickname"]}({Context.Items["login"]}) send message in main chat" :
                                                                              $"User {Context.Items["nickname"]}({Context.Items["login"]}) responded to the message in main chat";
            Log.Information(logMessage);
        }
        
        public async Task UpdateMessage(HubEditedMessage hubEditedMessage)
        {
            try
            {
                var mes = await _ravenSession.LoadAsync<Message>(hubEditedMessage.Id);

                if (mes.UserId == Context.Items["uid"]?.ToString() && 
                	(mes.Text.Trim()!=hubEditedMessage.Message.Trim() 
                	|| mes.IdQuotedMessage!= hubEditedMessage.IdQuotedMessage))
                {
                    mes.Text = hubEditedMessage.Message.Trim();
                    mes.LastEditTime = DateTimeOffset.Now;
                    mes.IdQuotedMessage = hubEditedMessage.IdQuotedMessage;
                    await _ravenSession.SaveChangesAsync();

                    var quotedReceiveMessage = await GetQuotedReceiveMessage(hubEditedMessage.IdQuotedMessage);
                   
                    await Clients.Group(_loginedGroup).SendAsync(new ReceiveEditedMessage()
                    {
                        Id = hubEditedMessage.Id,
                        Text = mes.Text,
                        LastEditTime = mes.LastEditTime.Value,
                        QuotedMessage= quotedReceiveMessage
                    });
                    Log.Information($"User {Context.Items["nickname"]}({Context.Items["login"]}) edited message in main chat");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        /// <summary>
        /// Удаление сообщений (для пользователя)
        /// </summary>
        /// <param name="idDeleteMessages"></param>
        /// <returns></returns>
        public async Task DeleteMessagesForMe(List<string> idDeleteMessages)
        {
            try
            {
                string uid = Context.Items["uid"].ToString();
                var messages = await _ravenSession.LoadAsync<Message>(idDeleteMessages);
                var listMessages = messages.Values.ToList();
                foreach (var item in listMessages)
                {
                    if (item.HideForUsers == null)
                    {
                        item.HideForUsers = new List<string>();
                    }

                    if (uid != null && !item.HideForUsers.Contains(uid))
                    {
                        item.HideForUsers.Add(uid);
                    }
                }
                await _ravenSession.SaveChangesAsync();
            }
            catch
            {

            }

        }
        /// <summary>
        /// Очистка чата (для пользователя)
        /// </summary>
        /// <param name="messagesHistoryDateBegin">дата/время после которого загружаются сообщения</param>
        /// <returns></returns>
        public async Task CleanChatForMe(string chatId)
        {
            try
            {
                Chat chat= await _ravenSession.LoadAsync<Chat>(chatId);
                string userId = Context.Items["uid"]?.ToString();
                ChatMember chatMember = chat.Members.FirstOrDefault(e => e.UserId == userId);
                chatMember.MessagesHistoryDateBegin = DateTimeOffset.UtcNow;
                await _ravenSession.StoreAsync(chat);
                await _ravenSession.SaveChangesAsync();
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
        /// <summary>
        /// Получает список вложений
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private async Task<List<AttachmentHubMold>> GetAttachments(Message message)
        {
            var Attachments = new List<AttachmentHubMold>();
            if (message.Attachments != null)
            {
                var attach = await _ravenSession.LoadAsync<Attachment>(message.Attachments);

                foreach (var id in message.Attachments)
                {
                    if (attach.TryGetValue(id, out var attachment))
                    {
                        Attachments.Add(Mapper.Map<AttachmentHubMold>(attachment));
                    }
                }
            }
            return Attachments;
        }
        /// <summary>
        /// Получает ReceiveMessage из баззы данных по ID
        /// </summary>
        /// <param name="IdQuotedMessage"></param>
        /// <returns></returns>
        private async Task<ReceiveMessage> GetQuotedReceiveMessage(string IdQuotedMessage)
        {
            ReceiveMessage quotedReceiveMessage;

           

            if (!IdQuotedMessage.IsNullOrEmpty())
            {
                var quotedMessage = await _ravenSession.LoadAsync<Message>(IdQuotedMessage);
                var user = await _ravenSession.LoadAsync<User>(quotedMessage.UserId);
                quotedReceiveMessage = new ReceiveMessage()
                {
                    Id = quotedMessage.Id,
                    UserLogin = user.Login,
                    UserNickname = user.DisplayName,
                    Text = quotedMessage.Text,
                    PostTime = quotedMessage.PostTime,
                    ChatId = quotedMessage.ChatId,
                    UserId = quotedMessage.UserId,
                    Attachments = await GetAttachments(quotedMessage),
                };
            }
            else quotedReceiveMessage = null;
            return quotedReceiveMessage;
        }
    }
}
