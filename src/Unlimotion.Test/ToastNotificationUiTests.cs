using System.Threading.Tasks;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class ToastNotificationUiTests
{
    [Test]
    public async Task MainScreen_ErrorToast_RendersAndCloseButtonRemovesMessage()
    {
        await ToastNotificationUiContract.AssertErrorToastRendersAndCanBeClosedAsync();
    }
}
