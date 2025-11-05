using System;
using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds
{
    [Description("Аудит входа")]
    public class UserLoginAudit
    {
        public UserLoginAudit()
        {
            Id = string.Empty;
            IpAddress = string.Empty;
            SessionId = string.Empty;
            NameVersionClient = string.Empty;
            OperatingSystem = string.Empty;
        }

        [Description("Идентификатор пользователя")]
        public string Id { get; set; }
        [Description("IP-адрес")]
        public string IpAddress { get; set; }
        [Description("Id текущей сессии")]
        public string SessionId { get; set; }
        [Description("Название и версия клиента")]
        public string NameVersionClient { get; set; }
        [Description("Операционная система")]
        public string OperatingSystem { get; set; }
        [Description("Дата входа")]
        public DateTime DateOfEntry { get; set; }
    }
}