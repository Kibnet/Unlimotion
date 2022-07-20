using System;
using System.Collections.Generic;

namespace Unlimotion.Server.Domain
{
    public class Message
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string Text { get; set; }
        public DateTimeOffset PostTime { get; set; }
        public string ChatId { get; set; }
        public DateTimeOffset? LastEditTime { get; set; }
        public List<string> Attachments { get; set; }
        public string IdQuotedMessage { get; set; }
        public List<string> HideForUsers { get; set; }
    }
}