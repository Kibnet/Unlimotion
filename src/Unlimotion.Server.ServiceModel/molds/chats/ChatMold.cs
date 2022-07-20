using System.Collections.Generic;
using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds.Chats
{
    [Description("Чат")]
    public class ChatMold
    {
        [Description("Идентификатор")]
        public string Id { get; set; }
        [Description("Владелец")]
        public string OwnerId { get; set; }
        [Description("Тип чата")]
        public ChatTypeMold ChatType { get; set; }
        [Description("Список членов")]
        public List<ChatMemberMold> Members { get; set; }
        [Description("Название чата")]
        public string ChatName { get; set; }
    }

    [Description("Тип чата")]
    public enum ChatTypeMold
    {
        [Description("Личный")]
        Private = 1,
        [Description("Групповой")]
        Public
    }
}
