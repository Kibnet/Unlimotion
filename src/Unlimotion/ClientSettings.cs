using System;

namespace Unlimotion;

public class ClientSettings
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTimeOffset ExpireTime { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;
}
