using System;
using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds
{
    [Description("Пара токенов авторизации")]
    public class TokenResult
    {
        [Description("Токен для авторизованных запросов")]
        public string AccessToken { get; set; }

        [Description("Токен для обновления токенов")]
        public string RefreshToken { get; set; }

        public DateTimeOffset ExpireTime { get; set; }
    }
}
