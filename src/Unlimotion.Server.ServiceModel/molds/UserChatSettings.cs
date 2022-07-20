using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds
{
    [Description("Настройки пользователя")]
    public class UserChatSettings
    {
        [Description("Индификатор настройки пользователя")]
        public string Id { get; set; }
        [Description("Настройка отправки сообщения пользователем")]
        public bool SendingMessageByEnterKey { get; set; }
    }
}
