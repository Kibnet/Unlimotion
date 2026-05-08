using System;

namespace Unlimotion.Domain
{
    public class LoginAudit
    {
        public string Id { get; set; } = null!;
        public string IpAddress { get; set; } = null!;
        public string SessionId { get; set; } = null!;
        public string NameVersionClient { get; set; } = null!;
        public string OperatingSystem { get; set; } = null!;
        public DateTime DateOfEntry { get; set; }
    }
}
