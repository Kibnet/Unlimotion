namespace Unlimotion.ViewModel;

public sealed record RemoteConnectionTypeSwitchResult(
    string RemoteName,
    string RemoteUrl,
    string AuthType,
    bool CreatedRemote);
