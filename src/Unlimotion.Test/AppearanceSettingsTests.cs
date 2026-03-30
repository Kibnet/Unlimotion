using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public class AppearanceSettingsTests
{
    [Test]
    public async System.Threading.Tasks.Task DerivedUiSizes_DefaultToExpectedValues()
    {
        await Assert.That(AppearanceSettings.GetTabFontSize(AppearanceSettings.DefaultFontSize)).IsEqualTo(14);
        await Assert.That(AppearanceSettings.GetTabMinHeight(AppearanceSettings.DefaultFontSize)).IsEqualTo(28);
        await Assert.That(AppearanceSettings.GetSearchControlHeight(AppearanceSettings.DefaultFontSize)).IsEqualTo(36);
        await Assert.That(AppearanceSettings.GetSearchClearButtonSize(AppearanceSettings.DefaultFontSize)).IsEqualTo(30);
        await Assert.That(AppearanceSettings.GetSearchClearIconFontSize(AppearanceSettings.DefaultFontSize)).IsEqualTo(14);
        await Assert.That(AppearanceSettings.GetSearchBarMinWidth(AppearanceSettings.DefaultFontSize)).IsEqualTo(328);
        await Assert.That(AppearanceSettings.GetFloatingControlMinHeight(AppearanceSettings.DefaultFontSize)).IsEqualTo(42);
    }

    [Test]
    public async System.Threading.Tasks.Task DerivedUiSizes_AreClampedForLargeFontSizes()
    {
        await Assert.That(AppearanceSettings.GetTabFontSize(24)).IsEqualTo(18);
        await Assert.That(AppearanceSettings.GetTabMinHeight(24)).IsEqualTo(32);
        await Assert.That(AppearanceSettings.GetSearchControlHeight(24)).IsEqualTo(48);
        await Assert.That(AppearanceSettings.GetSearchClearButtonSize(24)).IsEqualTo(42);
        await Assert.That(AppearanceSettings.GetSearchClearIconFontSize(24)).IsEqualTo(22);
        await Assert.That(AppearanceSettings.GetSearchBarMinWidth(24)).IsEqualTo(376);
        await Assert.That(AppearanceSettings.GetFloatingControlMinHeight(24)).IsEqualTo(54);
    }
}
