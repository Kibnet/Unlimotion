using System;
using SignalR.EasyUse.Interface;

namespace Unlimotion.Interface
{
    public class LogOn : IClientMethod
    {
        public string Id { get; set; } = null!;
        public string UserLogin { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public DateTimeOffset ExpireTime { get; set; }
        public enum LogOnStatus
        {
            Ok = 200,
            ErrorUserNotFound = 404,
            ErrorExpiredToken = 419,
            InternalError = 500
        }

        public LogOnStatus Error { get; set; }
    }
}
