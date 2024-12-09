using System;

namespace Unlimotion.Domain
{
    public class LoginAudit
    {
        public string Id { get; set; }
        public string IpAddress { get; set; }
        public string SessionId { get; set; }
        public string NameVersionClient { get; set; }
        public string OperatingSystem { get; set; }
        public DateTime DateOfEntry { get; set; }
    }
}