namespace Unlimotion.ViewModel;

public enum ApplicationUpdateState
{
    Unsupported,
    Idle,
    Checking,
    NoUpdates,
    UpdateAvailable,
    Downloading,
    ReadyToApply,
    Applying,
    Error
}
