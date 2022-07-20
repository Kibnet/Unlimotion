using System.Collections.Generic;

namespace Unlimotion.Interface
{
    public class HubMessage
    {
        public HubMessage() { }

        public HubMessage(string chatId, string message, string idQuotedMessage)
        {
            ChatId = chatId;
            Message = message;
            IdQuotedMessage = idQuotedMessage;
        }

        public HubMessage(string chatId, string message, List<AttachmentHubMold> attachment, string idQuotedMessage)
        {
            ChatId = chatId;
            Message = message;
            Attachments = attachment;   
            IdQuotedMessage = idQuotedMessage;
        }

        public string Message { get; set; }
        public string ChatId { get; set; }
        public string IdQuotedMessage { get; set; }
        public List<AttachmentHubMold> Attachments { get; set; }
    }
}
