using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using DomainTaskItem = Unlimotion.Domain.TaskItem;

namespace Unlimotion.Test;

public class FileUserProfileStorageTests
{
    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "UnlimotionProfileTests", Guid.NewGuid().ToString("N"));

    [Test]
    public async Task SaveThenLoad_RoundTripsAllFields()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            var birthday = new DateTimeOffset(1990, 5, 17, 0, 0, 0, TimeSpan.Zero);
            await storage.Save(new UserProfile
            {
                Id = "local-user",
                DisplayName = "Alex",
                FullName = "Alexandra Smith",
                Email = "alex@example.com",
                Birthday = birthday,
                AboutMe = "Task wrangler."
            });

            var loaded = await storage.Load("local-user");

            await Assert.That(loaded).IsNotNull();
            await Assert.That(loaded!.DisplayName).IsEqualTo("Alex");
            await Assert.That(loaded.FullName).IsEqualTo("Alexandra Smith");
            await Assert.That(loaded.Email).IsEqualTo("alex@example.com");
            await Assert.That(loaded.Birthday).IsEqualTo(birthday);
            await Assert.That(loaded.AboutMe).IsEqualTo("Task wrangler.");
            await Assert.That(loaded.UpdatedDateTime).IsNotNull();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task SaveThenLoad_RoundTripsCropFraming()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            await storage.Save(new UserProfile
            {
                Id = "local-user",
                DisplayName = "Alex",
                AvatarPath = "Users/avatars/local-user-1.png",
                AvatarZoom = 2.5,
                AvatarOffsetX = 0.3,
                AvatarOffsetY = -0.2
            });

            var loaded = await storage.Load("local-user");

            await Assert.That(loaded!.AvatarZoom).IsEqualTo(2.5);
            await Assert.That(loaded.AvatarOffsetX).IsEqualTo(0.3);
            await Assert.That(loaded.AvatarOffsetY).IsEqualTo(-0.2);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task Load_ReturnsNull_WhenProfileMissing()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            await Assert.That(await storage.Load("nobody")).IsNull();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task LoadAll_ReturnsEveryStoredProfile()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            await storage.Save(new UserProfile { Id = "alex", DisplayName = "Alex" });
            await storage.Save(new UserProfile { Id = "sam", DisplayName = "Sam" });

            var all = await storage.LoadAll();

            await Assert.That(all.Count).IsEqualTo(2);
            await Assert.That(all.Any(p => p.Id == "alex")).IsTrue();
            await Assert.That(all.Any(p => p.Id == "sam")).IsTrue();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task Delete_RemovesProfile()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            await storage.Save(new UserProfile { Id = "alex", DisplayName = "Alex" });

            var removed = await storage.Delete("alex");

            await Assert.That(removed).IsTrue();
            await Assert.That(await storage.Load("alex")).IsNull();
            await Assert.That(await storage.Delete("alex")).IsFalse();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task ImportAvatar_CopiesImage_ReturnsRelativePath_AndReplacesPrevious()
    {
        var root = NewTempRoot();
        var source = Path.Combine(root, "source.png");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(source, new byte[] { 1, 2, 3, 4 });

            var storage = new FileUserProfileStorage(root);
            var relative = await storage.ImportAvatar("local-user", source);

            await Assert.That(relative.StartsWith("Users/avatars/", StringComparison.Ordinal)).IsTrue();
            await Assert.That(relative.Contains('\\')).IsFalse();

            var absolute = storage.ResolveAvatarAbsolutePath(relative);
            await Assert.That(absolute).IsNotNull();
            await Assert.That(File.Exists(absolute!)).IsTrue();

            // Re-import must leave exactly one avatar file for the user (no orphans) with a new name.
            var relative2 = await storage.ImportAvatar("local-user", source);
            await Assert.That(relative2).IsNotEqualTo(relative);

            var avatarFiles = Directory.GetFiles(storage.AvatarsDirectory, "local-user*");
            await Assert.That(avatarFiles.Length).IsEqualTo(1);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task ImportAvatar_DoesNotTouchOtherUsersAvatarWithSharedPrefix()
    {
        var root = NewTempRoot();
        var source = Path.Combine(root, "source.png");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(source, new byte[] { 1, 2, 3 });

            var storage = new FileUserProfileStorage(root);
            // "ab" shares a prefix with "a" — importing/re-importing "a" must not delete "ab"'s avatar.
            var abRelative = await storage.ImportAvatar("ab", source);
            await storage.ImportAvatar("a", source);
            await storage.ImportAvatar("a", source); // triggers RemoveExistingAvatars("a")

            await Assert.That(storage.ResolveAvatarAbsolutePath(abRelative)).IsNotNull();
            await Assert.That(Directory.GetFiles(storage.AvatarsDirectory, "ab-*").Length).IsEqualTo(1);
            await Assert.That(Directory.GetFiles(storage.AvatarsDirectory, "a-*").Length).IsEqualTo(1);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task ResolveAvatarAbsolutePath_RejectsPathsOutsideStorageRoot()
    {
        var root = NewTempRoot();
        try
        {
            Directory.CreateDirectory(root);
            var storage = new FileUserProfileStorage(root);

            // Absolute path from a hand-edited profile is refused outright.
            var absoluteOutside = Path.Combine(Path.GetTempPath(), "unlimotion-outside.png");
            await Assert.That(storage.ResolveAvatarAbsolutePath(absoluteOutside)).IsNull();

            // A relative "../" escape is refused even if such a file exists.
            await Assert.That(storage.ResolveAvatarAbsolutePath("../../secret.png")).IsNull();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task ResolveAvatarAbsolutePath_ReturnsNull_WhenUnsetOrMissing()
    {
        var root = NewTempRoot();
        try
        {
            var storage = new FileUserProfileStorage(root);
            await Assert.That(storage.ResolveAvatarAbsolutePath(null)).IsNull();
            await Assert.That(storage.ResolveAvatarAbsolutePath("Users/avatars/none.png")).IsNull();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Test]
    public async Task Profiles_AreInvisibleToTheTaskScan()
    {
        var root = NewTempRoot();
        try
        {
            var taskStorage = new FileStorage(root);
            await taskStorage.Save(new DomainTaskItem { Id = "task-1", Title = "First", UserId = "local-user" });
            await taskStorage.Save(new DomainTaskItem { Id = "task-2", Title = "Second", UserId = "local-user" });

            // Profile + avatar land in the Users sub-folder of the same tasks directory.
            var profileStorage = new FileUserProfileStorage(root);
            await profileStorage.Save(new UserProfile { Id = "local-user", DisplayName = "Alex" });
            var avatarSource = Path.Combine(root, "avatar.png");
            await File.WriteAllBytesAsync(avatarSource, new byte[] { 9, 9, 9 });
            await profileStorage.ImportAvatar("local-user", avatarSource);

            var scanned = new List<DomainTaskItem>();
            await foreach (var task in taskStorage.GetAll())
            {
                scanned.Add(task);
            }

            await Assert.That(scanned.Count).IsEqualTo(2);
            await Assert.That(scanned.Any(t => t.Id == "local-user")).IsFalse();
            await Assert.That(scanned.All(t => t.Id.StartsWith("task-", StringComparison.Ordinal))).IsTrue();
        }
        finally
        {
            SafeDelete(root);
        }
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
