using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Unlimotion.Domain;

namespace Unlimotion.ViewModel;

/// <summary>
/// File-backed <see cref="IUserProfileStorage"/>. Profiles are JSON files in a <c>Users</c> sub-folder
/// of the task storage folder, e.g. <c>&lt;Tasks&gt;/Users/local-user.json</c>. Because the folder is a
/// sub-directory it is never picked up by the task scan (<c>TopDirectoryOnly</c>) or the file watcher
/// (<c>IncludeSubdirectories = false</c>), yet it is inside the same git-backed folder so it is
/// versioned and pushed together with the tasks.
/// </summary>
public class FileUserProfileStorage : IUserProfileStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string StorageRoot { get; }
    public string ProfilesDirectory { get; }
    public string AvatarsDirectory { get; }

    public FileUserProfileStorage(string storageRoot)
    {
        StorageRoot = string.IsNullOrWhiteSpace(storageRoot) ? "Tasks" : storageRoot;
        ProfilesDirectory = Path.Combine(StorageRoot, UserProfileSettings.UsersFolderName);
        AvatarsDirectory = Path.Combine(ProfilesDirectory, UserProfileSettings.AvatarsFolderName);
    }

    public Task<UserProfile?> Load(string userId)
    {
        var id = UserProfileSettings.NormalizeUserId(userId);
        var path = GetProfilePath(id);
        return Task.FromResult(ReadProfile(path));
    }

    public Task<IReadOnlyList<UserProfile>> LoadAll()
    {
        var profiles = new List<UserProfile>();
        if (Directory.Exists(ProfilesDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(
                         ProfilesDirectory,
                         "*" + UserProfileSettings.ProfileFileExtension,
                         SearchOption.TopDirectoryOnly))
            {
                var profile = ReadProfile(file);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Id))
                {
                    profiles.Add(profile);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<UserProfile>>(profiles);
    }

    public Task<UserProfile> Save(UserProfile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        profile.Id = UserProfileSettings.NormalizeUserId(profile.Id);
        if (profile.CreatedDateTime == default)
        {
            profile.CreatedDateTime = DateTimeOffset.UtcNow;
        }

        profile.UpdatedDateTime = DateTimeOffset.UtcNow;

        Directory.CreateDirectory(ProfilesDirectory);
        var path = GetProfilePath(profile.Id);
        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        File.WriteAllText(path, json);
        return Task.FromResult(profile);
    }

    public Task<bool> Delete(string userId)
    {
        var id = UserProfileSettings.NormalizeUserId(userId);
        var removed = false;

        var profilePath = GetProfilePath(id);
        if (File.Exists(profilePath))
        {
            File.Delete(profilePath);
            removed = true;
        }

        RemoveExistingAvatars(id);
        return Task.FromResult(removed);
    }

    public Task<string> ImportAvatar(string userId, string sourceImagePath)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
        {
            throw new FileNotFoundException("Avatar source image was not found.", sourceImagePath);
        }

        var id = UserProfileSettings.NormalizeUserId(userId);
        var extension = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        Directory.CreateDirectory(AvatarsDirectory);

        // Drop any previous avatar so we never accumulate orphans, and use a unique file name so the
        // UI's path-based image binding refreshes even when the same source file is re-imported.
        RemoveExistingAvatars(id);
        var fileName = $"{id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}";
        var destination = Path.Combine(AvatarsDirectory, fileName);
        File.Copy(sourceImagePath, destination, overwrite: true);

        var relative = Path.GetRelativePath(StorageRoot, destination).Replace('\\', '/');
        return Task.FromResult(relative);
    }

    public string? ResolveAvatarAbsolutePath(string? avatarPath)
    {
        if (string.IsNullOrWhiteSpace(avatarPath))
        {
            return null;
        }

        // Only trust a relative path that resolves to a real file *inside* the storage root. This
        // rejects absolute paths and "../" escapes from a hand-edited or corrupted profile file that
        // could otherwise point the avatar image binding at an arbitrary file on disk.
        if (Path.IsPathRooted(avatarPath))
        {
            return null;
        }

        var rootFull = Path.GetFullPath(StorageRoot);
        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(rootFull, avatarPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            return null;
        }

        return File.Exists(candidate) ? candidate : null;
    }

    private string GetProfilePath(string userId) =>
        Path.Combine(ProfilesDirectory, userId + UserProfileSettings.ProfileFileExtension);

    private void RemoveExistingAvatars(string userId)
    {
        if (!Directory.Exists(AvatarsDirectory))
        {
            return;
        }

        // Avatar files are named "{userId}-{unixMillis}{ext}". Match that exact shape instead of a
        // "{userId}*" glob so we never delete another user's avatar whose id merely shares this prefix
        // (e.g. removing "a"'s avatar must not touch "ab"'s), and so a userId containing glob
        // metacharacters can't turn the pattern into a wildcard that wipes every avatar.
        var prefix = userId + "-";
        foreach (var file in Directory.EnumerateFiles(AvatarsDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length <= prefix.Length ||
                !name.StartsWith(prefix, StringComparison.Ordinal) ||
                !name[prefix.Length..].All(char.IsAsciiDigit))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort — a locked/read-only previous avatar shouldn't block import or delete.
            }
        }
    }

    private static UserProfile? ReadProfile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserProfile>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
