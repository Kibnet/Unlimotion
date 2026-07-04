using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Test;

public class UserProfileViewModelTests
{
    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "UnlimotionProfileVmTests", Guid.NewGuid().ToString("N"));

    [Test]
    public async Task Constructor_LoadsExistingProfileFromStorage()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            await storage.Save(new UserProfile
            {
                Id = UserProfileSettings.DefaultUserId,
                DisplayName = "Saved Alex",
                Email = "alex@example.com"
            });

            var vm = new UserProfileViewModel(storage);

            await Assert.That(vm.DisplayName).IsEqualTo("Saved Alex");
            await Assert.That(vm.Email).IsEqualTo("alex@example.com");
            await Assert.That(vm.CurrentUserId).IsEqualTo(UserProfileSettings.DefaultUserId);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task SaveAsync_PersistsEditedFields()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage)
            {
                DisplayName = "New Name",
                Email = "new@example.com",
                AboutMe = "  Hi there  "
            };

            await vm.SaveAsync();

            var reloaded = await storage.Load(vm.CurrentUserId);
            await Assert.That(reloaded).IsNotNull();
            await Assert.That(reloaded!.DisplayName).IsEqualTo("New Name");
            await Assert.That(reloaded.Email).IsEqualTo("new@example.com");
            await Assert.That(reloaded.AboutMe).IsEqualTo("Hi there");
            await Assert.That(vm.Status).IsEqualTo(L10n.Get("ProfileSaved"));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task SaveAsync_DoesNotPersist_WhenDisplayNameMissing()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage)
            {
                DisplayName = "",
                Email = "ok@example.com"
            };

            await vm.SaveAsync();

            await Assert.That(vm.CanSave).IsFalse();
            await Assert.That(await storage.Load(vm.CurrentUserId)).IsNull();
            await Assert.That(vm.Status).IsEqualTo(L10n.Get("ProfileDisplayNameRequired"));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task SaveAsync_DoesNotPersist_WhenEmailInvalid()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage)
            {
                DisplayName = "Alex",
                Email = "not-an-email"
            };

            await vm.SaveAsync();

            await Assert.That(vm.CanSave).IsFalse();
            await Assert.That(await storage.Load(vm.CurrentUserId)).IsNull();
            await Assert.That(vm.Status).IsEqualTo(L10n.Get("ProfileEmailInvalid"));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task Constructor_UsesCurrentUserIdFromConfiguration()
    {
        var root = NewTempRoot();
        var configPath = Path.Combine(root, "settings.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(configPath, "{}");
            var configuration = WritableJsonConfigurationFabric.Create(configPath, reloadOnChange: false);
            configuration.GetSection(UserProfileSettings.SectionName)
                .GetSection(UserProfileSettings.CurrentUserIdKey)
                .Set("alex");

            var storage = new FileUserProfileStorage(root);
            await storage.Save(new UserProfile { Id = "alex", DisplayName = "Alex Config" });

            var vm = new UserProfileViewModel(storage, configuration);

            await Assert.That(vm.CurrentUserId).IsEqualTo("alex");
            await Assert.That(vm.DisplayName).IsEqualTo("Alex Config");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task PickAvatarAsync_ImportsImageAndPersistsPath()
    {
        var root = NewTempRoot();
        var source = Path.Combine(root, "pic.png");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(source, new byte[] { 1, 2, 3 });

            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage)
            {
                DisplayName = "Alex",
                Dialogs = new FakeDialogs { FileToReturn = source }
            };

            await vm.PickAvatarAsync();

            await Assert.That(vm.HasAvatar).IsTrue();
            await Assert.That(vm.AvatarRelativePath!.StartsWith("Users/avatars/", StringComparison.Ordinal)).IsTrue();

            var reloaded = await storage.Load(vm.CurrentUserId);
            await Assert.That(reloaded!.AvatarPath).IsEqualTo(vm.AvatarRelativePath);
            await Assert.That(vm.Status).IsEqualTo(L10n.Get("ProfileAvatarUpdated"));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task PickAvatarAsync_RejectsUnsupportedFile()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage)
            {
                DisplayName = "Alex",
                Dialogs = new FakeDialogs { FileToReturn = "notes.txt" }
            };

            await vm.PickAvatarAsync();

            await Assert.That(vm.HasAvatar).IsFalse();
            await Assert.That(vm.Status).IsEqualTo(L10n.Get("ProfileAvatarUnsupported"));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task RemoveAvatarAsync_ClearsAvatar()
    {
        var root = NewTempRoot();
        var source = Path.Combine(root, "pic.png");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(source, new byte[] { 4, 5, 6 });

            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage)
            {
                DisplayName = "Alex",
                Dialogs = new FakeDialogs { FileToReturn = source }
            };
            await vm.PickAvatarAsync();
            await Assert.That(vm.HasAvatar).IsTrue();

            await vm.RemoveAvatarAsync();

            await Assert.That(vm.HasAvatar).IsFalse();
            await Assert.That(vm.AvatarRelativePath).IsNull();
            var reloaded = await storage.Load(vm.CurrentUserId);
            await Assert.That(reloaded!.AvatarPath).IsNull();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task DisplayNameMissing_TracksDisplayName()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage);

            await Assert.That(vm.DisplayNameMissing).IsTrue();

            vm.DisplayName = "Alex";
            await Assert.That(vm.DisplayNameMissing).IsFalse();

            // A present name with an invalid email is NOT a "display name missing" case.
            vm.Email = "not-an-email";
            await Assert.That(vm.DisplayNameMissing).IsFalse();
            await Assert.That(vm.CanSave).IsFalse();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task SaveAsync_SetsStatus_WhenStorageThrows()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new ThrowingProfileStorage(new FileUserProfileStorage(root));
            var vm = new UserProfileViewModel(storage)
            {
                DisplayName = "Alex",
                Email = "alex@example.com"
            };

            await vm.SaveAsync(); // must not throw out of the command

            await Assert.That(vm.Status).IsEqualTo(L10n.Format("ProfileSaveFailed", "disk full"));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task SaveAsync_PersistsCropFraming()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage)
            {
                DisplayName = "Alex",
                AvatarZoom = 2.0,
                AvatarOffsetX = 0.25,
                AvatarOffsetY = -0.1
            };

            await vm.SaveAsync();

            var reloaded = await storage.Load(vm.CurrentUserId);
            await Assert.That(reloaded!.AvatarZoom).IsEqualTo(2.0);
            await Assert.That(reloaded.AvatarOffsetX).IsEqualTo(0.25);
            await Assert.That(reloaded.AvatarOffsetY).IsEqualTo(-0.1);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task PickAvatarAsync_ResetsCropFraming()
    {
        var root = NewTempRoot();
        var source = Path.Combine(root, "pic.png");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(source, new byte[] { 1, 2, 3 });

            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage)
            {
                DisplayName = "Alex",
                AvatarZoom = 3.0,
                AvatarOffsetX = 0.4,
                AvatarOffsetY = 0.4,
                Dialogs = new FakeDialogs { FileToReturn = source }
            };

            await vm.PickAvatarAsync();

            await Assert.That(vm.AvatarZoom).IsEqualTo(UserProfileSettings.MinAvatarZoom);
            await Assert.That(vm.AvatarOffsetX).IsEqualTo(0);
            await Assert.That(vm.AvatarOffsetY).IsEqualTo(0);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task Constructor_ClampsOutOfRangeZoomFromFile()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            // Older profiles have no crop fields → AvatarZoom deserializes as 0; must clamp to 1.
            await storage.Save(new UserProfile { Id = UserProfileSettings.DefaultUserId, DisplayName = "Alex", AvatarZoom = 0 });

            var vm = new UserProfileViewModel(storage);

            await Assert.That(vm.AvatarZoom).IsEqualTo(UserProfileSettings.MinAvatarZoom);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task Initials_DerivedFromDisplayName()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            var vm = new UserProfileViewModel(storage) { DisplayName = "Alex Smith" };
            await Assert.That(vm.Initials).IsEqualTo("AS");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    private sealed class ThrowingProfileStorage : IUserProfileStorage
    {
        private readonly IUserProfileStorage _inner;

        public ThrowingProfileStorage(IUserProfileStorage inner) => _inner = inner;

        public string StorageRoot => _inner.StorageRoot;
        public string ProfilesDirectory => _inner.ProfilesDirectory;
        public string AvatarsDirectory => _inner.AvatarsDirectory;
        public Task<UserProfile?> Load(string userId) => _inner.Load(userId);
        public Task<IReadOnlyList<UserProfile>> LoadAll() => _inner.LoadAll();
        public Task<UserProfile> Save(UserProfile profile) => throw new IOException("disk full");
        public Task<bool> Delete(string userId) => _inner.Delete(userId);
        public Task<string> ImportAvatar(string userId, string sourceImagePath) => _inner.ImportAvatar(userId, sourceImagePath);
        public string? ResolveAvatarAbsolutePath(string? avatarPath) => _inner.ResolveAvatarAbsolutePath(avatarPath);
    }

    private sealed class FakeDialogs : IDialogs
    {
        public string? FileToReturn { get; set; }

        public Task<string> ShowOpenFolderDialogAsync(string? title = null, string? directory = null)
            => Task.FromResult(string.Empty);

        public Task<string> ShowOpenFileDialogAsync(
            string? title = null,
            IReadOnlyList<string>? allowedExtensions = null,
            string? directory = null)
            => Task.FromResult(FileToReturn ?? string.Empty);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
        }
    }
}
