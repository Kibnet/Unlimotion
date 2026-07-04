using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.Domain;

namespace Unlimotion.ViewModel;

/// <summary>
/// Persistence for local <see cref="UserProfile"/> records. Kept separate from the task
/// <see cref="ITaskStorage"/> so profiles never mix into the task graph, while still living next to
/// the tasks (inside the same git-backed folder).
/// </summary>
public interface IUserProfileStorage
{
    /// <summary>Root the avatar relative paths are resolved against (the task storage folder).</summary>
    string StorageRoot { get; }

    /// <summary>Folder that holds the profile files (<c>&lt;StorageRoot&gt;/Users</c>).</summary>
    string ProfilesDirectory { get; }

    /// <summary>Folder that holds copied avatar images (<c>&lt;StorageRoot&gt;/Users/avatars</c>).</summary>
    string AvatarsDirectory { get; }

    /// <summary>Loads a single profile by id, or null when it does not exist / cannot be read.</summary>
    Task<UserProfile?> Load(string userId);

    /// <summary>Loads every stored profile (top-level files only; avatars are ignored).</summary>
    Task<IReadOnlyList<UserProfile>> LoadAll();

    /// <summary>Writes the profile, stamping <see cref="UserProfile.UpdatedDateTime"/>.</summary>
    Task<UserProfile> Save(UserProfile profile);

    /// <summary>Deletes the profile file (and its avatar). Returns false when nothing was removed.</summary>
    Task<bool> Delete(string userId);

    /// <summary>
    /// Copies <paramref name="sourceImagePath"/> into the avatars folder, removing the user's previous
    /// avatar, and returns the new path relative to <see cref="StorageRoot"/> (forward-slash separated).
    /// </summary>
    Task<string> ImportAvatar(string userId, string sourceImagePath);

    /// <summary>Resolves a stored (relative) avatar path into an absolute path, or null when unset/missing.</summary>
    string? ResolveAvatarAbsolutePath(string? avatarPath);
}
