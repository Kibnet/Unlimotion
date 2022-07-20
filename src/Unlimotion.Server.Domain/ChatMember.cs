using System;
using System.Collections.Generic;
using System.Text;

namespace Unlimotion.Server.Domain
{
    public class ChatMember
    {
        public string UserId { get; set; }
        public ChatMemberRole UserRole { get; set; }
        public DateTimeOffset MessagesHistoryDateBegin { get; set; }
    }

    public enum ChatMemberRole
    {
        Moderator = 1,
        Participient,
        Viewer
    }
}
