using System;
using System.Collections.Generic;
using System.Text;

namespace Unlimotion.Server.Domain
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