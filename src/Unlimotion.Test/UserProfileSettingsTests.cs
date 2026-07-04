using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public class UserProfileSettingsTests
{
    [Test]
    public async Task CanSave_RequiresDisplayName_AndValidOrEmptyEmail()
    {
        await Assert.That(UserProfileSettings.CanSave("Alex", null)).IsTrue();
        await Assert.That(UserProfileSettings.CanSave("Alex", "")).IsTrue();
        await Assert.That(UserProfileSettings.CanSave("Alex", "alex@example.com")).IsTrue();

        await Assert.That(UserProfileSettings.CanSave("", "alex@example.com")).IsFalse();
        await Assert.That(UserProfileSettings.CanSave("   ", null)).IsFalse();
        await Assert.That(UserProfileSettings.CanSave("Alex", "not-an-email")).IsFalse();
    }

    [Test]
    public async Task IsEmailValidOrEmpty_AcceptsEmptyAndWellFormed()
    {
        await Assert.That(UserProfileSettings.IsEmailValidOrEmpty(null)).IsTrue();
        await Assert.That(UserProfileSettings.IsEmailValidOrEmpty("")).IsTrue();
        await Assert.That(UserProfileSettings.IsEmailValidOrEmpty("  a@b.co  ")).IsTrue();

        await Assert.That(UserProfileSettings.IsEmailValidOrEmpty("a@b")).IsFalse();
        await Assert.That(UserProfileSettings.IsEmailValidOrEmpty("a b@c.com")).IsFalse();
        await Assert.That(UserProfileSettings.IsEmailValidOrEmpty("@c.com")).IsFalse();
    }

    [Test]
    public async Task BuildInitials_UsesNameThenEmail_AndCapsAtTwoLetters()
    {
        await Assert.That(UserProfileSettings.BuildInitials("Alex Smith", null, null)).IsEqualTo("AS");
        await Assert.That(UserProfileSettings.BuildInitials("madonna", null, null)).IsEqualTo("M");
        await Assert.That(UserProfileSettings.BuildInitials(null, "Jane Q Doe", null)).IsEqualTo("JQ");
        await Assert.That(UserProfileSettings.BuildInitials(null, null, "john.doe@example.com")).IsEqualTo("JD");
        await Assert.That(UserProfileSettings.BuildInitials("", "", "")).IsEqualTo("?");
    }

    [Test]
    public async Task NormalizeUserId_FallsBackToDefault_AndStripsPathChars()
    {
        await Assert.That(UserProfileSettings.NormalizeUserId(null)).IsEqualTo(UserProfileSettings.DefaultUserId);
        await Assert.That(UserProfileSettings.NormalizeUserId("   ")).IsEqualTo(UserProfileSettings.DefaultUserId);
        await Assert.That(UserProfileSettings.NormalizeUserId("  alex  ")).IsEqualTo("alex");

        var sanitized = UserProfileSettings.NormalizeUserId("../../etc/passwd");
        await Assert.That(sanitized.Contains('/')).IsFalse();
        await Assert.That(sanitized.Contains('\\')).IsFalse();
    }

    [Test]
    public async Task NormalizeUserId_StripsGlobMetacharsAndSeparators_CrossPlatform()
    {
        // Glob metacharacters and path separators must be removed on every OS (not just Windows),
        // otherwise they flow into avatar file globs and cause cross-user deletion.
        await Assert.That(UserProfileSettings.NormalizeUserId("*")).IsEqualTo(UserProfileSettings.DefaultUserId);
        await Assert.That(UserProfileSettings.NormalizeUserId("a*b?c")).IsEqualTo("abc");
        await Assert.That(UserProfileSettings.NormalizeUserId("a/b\\c")).IsEqualTo("abc");
        await Assert.That(UserProfileSettings.NormalizeUserId("..")).IsEqualTo(UserProfileSettings.DefaultUserId);
        await Assert.That(UserProfileSettings.NormalizeUserId("local-user")).IsEqualTo("local-user");

        var guid = "a1b2c3d4-1234-5678-9abc-def012345678";
        await Assert.That(UserProfileSettings.NormalizeUserId(guid)).IsEqualTo(guid);

        var sanitized = UserProfileSettings.NormalizeUserId("../../etc/passwd");
        await Assert.That(sanitized.IndexOfAny(new[] { '/', '\\', '.', '*', '?', '[' })).IsEqualTo(-1);
    }

    [Test]
    public async Task ClampAvatarZoom_KeepsWithinRange()
    {
        await Assert.That(UserProfileSettings.ClampAvatarZoom(0.5)).IsEqualTo(UserProfileSettings.MinAvatarZoom);
        await Assert.That(UserProfileSettings.ClampAvatarZoom(0)).IsEqualTo(UserProfileSettings.MinAvatarZoom);
        await Assert.That(UserProfileSettings.ClampAvatarZoom(double.NaN)).IsEqualTo(UserProfileSettings.MinAvatarZoom);
        await Assert.That(UserProfileSettings.ClampAvatarZoom(100)).IsEqualTo(UserProfileSettings.MaxAvatarZoom);
        await Assert.That(UserProfileSettings.ClampAvatarZoom(2.5)).IsEqualTo(2.5);
    }

    [Test]
    public async Task ClampAvatarOffset_LimitsPanToImageCoverage()
    {
        // Square image, no zoom: cannot pan at all.
        await Assert.That(UserProfileSettings.ClampAvatarOffset(0.4, 1.0, 1.0)).IsEqualTo(0);

        // Square image at 2x: half of the extra size is pannable (±0.5).
        await Assert.That(UserProfileSettings.ClampAvatarOffset(0.9, 2.0, 1.0)).IsEqualTo(0.5);
        await Assert.That(UserProfileSettings.ClampAvatarOffset(-0.9, 2.0, 1.0)).IsEqualTo(-0.5);
        await Assert.That(UserProfileSettings.ClampAvatarOffset(0.2, 2.0, 1.0)).IsEqualTo(0.2);

        // Wide image (ratio 2) at 1x: the overflow along the wide axis is pannable (±0.5).
        await Assert.That(UserProfileSettings.ClampAvatarOffset(0.9, 1.0, 2.0)).IsEqualTo(0.5);

        await Assert.That(UserProfileSettings.ClampAvatarOffset(double.NaN, 2.0, 1.0)).IsEqualTo(0);
    }

    [Test]
    public async Task IsAllowedAvatarExtension_MatchesImagesCaseInsensitively()
    {
        await Assert.That(UserProfileSettings.IsAllowedAvatarExtension("photo.PNG")).IsTrue();
        await Assert.That(UserProfileSettings.IsAllowedAvatarExtension("a.jpeg")).IsTrue();
        await Assert.That(UserProfileSettings.IsAllowedAvatarExtension("note.txt")).IsFalse();
        await Assert.That(UserProfileSettings.IsAllowedAvatarExtension(null)).IsFalse();
    }
}
