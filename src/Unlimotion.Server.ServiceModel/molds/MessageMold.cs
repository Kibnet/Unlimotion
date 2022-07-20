using Unlimotion.Server.ServiceModel.Molds.Attachment;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds
{
    [Description("Сообщение со страницы сообщений")]
    public class MessageMold
    {
        [Description("Идентификатор сообщения")]
        public string Id { get; set; }

        [Description("Идентификатор пользователя")]
        public string UserId { get; set; }

        [Description("Имя пользователя")]
        public string UserNickName { get; set; }

        [Description("Текст сообщения")]
        public string Text { get; set; }

        [Description("Время публикации")]
        public DateTimeOffset PostTime { get; set; }

        [Description("Идентификатор чата")]
        public string ChatId { get; set; }

        [Description("Время последнего редактирования")]
        public DateTimeOffset? LastEditTime { get; set; }

        [Description("Вложения")]
        public List<AttachmentMold> Attachments { get; set; }

        [Description("Цитируемое сообщение")]
        public MessageMold QuotedMessage { get; set; }

        [Description("Скрыто для UserId из списка")]
        public List<string> HideForUsers { get; set; }
    }
}