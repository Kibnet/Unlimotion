namespace Unlimotion.Interface
{
    public class HubEditedMessage : HubMessage
    {
        public string Id { get; set; }
        public HubEditedMessage(string id, string chatId, string message, string idQuotedMessage)
        {
            Id = id;
            ChatId = chatId;
            Message = message;
            IdQuotedMessage = idQuotedMessage;
        }
    }
}