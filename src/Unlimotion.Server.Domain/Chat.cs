using System;
using System.Collections.Generic;
using System.Text;

namespace Unlimotion.Server.Domain
{
    /// <summary>
    /// Чат
    /// </summary>
    public class Chat
    {
        /// <summary>
        /// Идентификатор
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// Id владельца чата
        /// </summary>
        public string  OwnerId { get; set; }
        /// <summary>
        /// Тип чата из ChatType
        /// </summary>
        public ChatType ChatType { get; set; }
        /// <summary>
        /// Участники чата
        /// </summary>
        public List<ChatMember> Members { get; set; }
        /// <summary>
        /// Наименование чата
        /// </summary>
        public string ChatName { get; set; }
    }
        
    public enum ChatType
    {
        Private=1,
        Public
    }
}
