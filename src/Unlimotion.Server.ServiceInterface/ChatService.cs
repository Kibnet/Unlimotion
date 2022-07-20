using AutoMapper;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using ServiceStack;
using Unlimotion.Server.Domain;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds;
using Unlimotion.Server.ServiceModel.Molds.Attachment;
using Unlimotion.Server.ServiceModel.Molds.Chats;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Unlimotion.Server.ServiceInterface
{
    public class ChatService : Service
    {
        public IAsyncDocumentSession RavenSession { get; set; }
        public IMapper Mapper { get; set; }

        [Authenticate]
        public async Task<MessagePage> Get(GetMessages request)
        {
            var session = Request.ThrowIfUnauthorized();
            var userId = session?.UserAuthId;
            Chat chat = await RavenSession.LoadAsync<Chat>(request.ChatId);
            ChatMember chatMember = chat.Members.FirstOrDefault(e => e.UserId == userId);
            var messages = RavenSession.Query<Message>().Where(e => e.ChatId == request.ChatId).OrderByDescending(x => x.PostTime);
            var result = new MessagePage();

            if (request.BeforePostTime != null)
            {
                messages = messages.Where(x => x.PostTime.UtcDateTime < request.BeforePostTime.Value.UtcDateTime);
            }
            if (chatMember?.MessagesHistoryDateBegin != null)
            {
                messages = messages.Where(x => x.PostTime > chatMember.MessagesHistoryDateBegin);
            }
            messages = messages.Where(x => x.HideForUsers == null || !x.HideForUsers.Contains(userId));

            var pageSize = request.PageSize ?? 50;
            
            var docs = 
                await messages.Take(pageSize)
                    .Include(x => x.UserId)
                    .Include(s => s.Attachments)
                    .Include(i=>i.IdQuotedMessage)
                    .ToListAsync();

            result.Messages = new List<MessageMold>();
            foreach (var doc in docs)
            {
                var user = await RavenSession.LoadAsync<User>(doc.UserId);
                var message = Mapper.Map<MessageMold>(doc);

                message = await GetAttachments(doc, message);

                if (!doc.IdQuotedMessage.IsNullOrEmpty())
                {
                    var mes = await RavenSession.LoadAsync<Message>(doc.IdQuotedMessage);

                    message.QuotedMessage = Mapper.Map<MessageMold>(mes);
                    var userQuitedMessage = await RavenSession.LoadAsync<User>(message.QuotedMessage.UserId);

                    message.QuotedMessage = await GetAttachments(mes, message.QuotedMessage);

                    if (userQuitedMessage != null)
                    {
                        message.QuotedMessage.UserNickName = string.IsNullOrWhiteSpace(userQuitedMessage.DisplayName)
                            ? userQuitedMessage.Login : userQuitedMessage.DisplayName;
                    }

                }

                if (user != null)
                {
                    message.UserNickName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Login : user.DisplayName;
                }

                result.Messages.Add(message);
            }
            return result;
        }

        [Authenticate]
        public async Task<ChatPage> Get(GetChatsList request)
        {
            var session = Request.ThrowIfUnauthorized();
            var uid = session.UserAuthId;
            var chats = await RavenSession.Query<Chat>()
                .Where(chat => chat.Members.Any(member => member.UserId == uid))
                .ToListAsync();
            return new ChatPage
            {
                Chats = chats.Select(e => Mapper.Map<ChatMold>(e)).ToList()
            };
        }
        /// <summary>
        /// Получает все вложения сообщения, если они есть.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="messageMold"></param>
        /// <returns></returns>
        private async Task<MessageMold> GetAttachments(Message message, MessageMold messageMold)
        {
            if (message.Attachments != null)
            {
                var attach = await RavenSession.LoadAsync<Attachment>(message.Attachments);
                messageMold.Attachments = new List<AttachmentMold>();

                foreach (var id in message.Attachments)
                {
                    if (attach.TryGetValue(id, out var attachment))
                    {
                        messageMold.Attachments.Add(Mapper.Map<AttachmentMold>(attachment));
                    }
                    //TODO учесть в будущем показ потерянных файлов
                }
            }
            return messageMold;
        }
    }
}