using System;

namespace Unlimotion;

public class ClientSettings
{
    public string AccessToken { get; set; }

    public string RefreshToken { get; set; }

    public DateTimeOffset ExpireTime { get; set; }

    public string UserId { get; set; }

    public string Login { get; set; }
}