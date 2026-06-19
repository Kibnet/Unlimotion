using System.Threading.Tasks;

namespace Unlimotion.Test;

public class PlatformShellProjectContractTests
{
    [Test]
    public async Task AndroidProject_IncludesSharedUiReferenceAndNativeGitAssets()
    {
        await PlatformShellProjectContracts.AssertAndroidProjectIncludesSharedUiReferenceAndNativeGitAssetsAsync();
    }

    [Test]
    public async Task BrowserProject_UsesSharedUiAndBrowserAppStartupContract()
    {
        await PlatformShellProjectContracts.AssertBrowserProjectUsesSharedUiAndBrowserAppStartupContractAsync();
    }

    [Test]
    public async Task IosProject_UsesSharedUiAndAvaloniaDelegateContract()
    {
        await PlatformShellProjectContracts.AssertIosProjectUsesSharedUiAndAvaloniaDelegateContractAsync();
    }
}
