using SignalR.EasyUse.Interface;
using System;
using System.Collections.Generic;

namespace Unlimotion.Interface
{
    public class ReceiveMessage : IClientMethod
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string UserLogin { get; set; }
        public string UserNickname { get; set; }
        public string Text { get; set; }
        public DateTimeOffset PostTime { get; set; }
        public string ChatId { get; set; }
        public List<AttachmentHubMold> Attachments { get; set; }
        public ReceiveMessage QuotedMessage { get; set; }
    }
}
