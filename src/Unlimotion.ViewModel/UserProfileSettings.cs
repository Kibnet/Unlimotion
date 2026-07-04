using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Unlimotion.ViewModel;

/// <summary>
/// Constants and small pure helpers for the personal profile ("личный кабинет").
/// Mirrors the shape of <see cref="AppearanceSettings"/>: one place for section/keys and validation
/// so the view-model, storage and tests all agree.
/// </summary>
public static class UserProfileSettings
{
    /// <summary>Config section that remembers which profile is currently active on this device.</summary>
    public const string SectionName = "Profile";

    public const string CurrentUserIdKey = "CurrentUserId";

    /// <summary>Id (and file name) of the default profile. Matches the historical task author "local-user".</summary>
    public const string DefaultUserId = "local-user";

    /// <summary>Sub-folder of the task storage that holds profile files. Skipped by the task scan and watcher.</summary>
    public const string UsersFolderName = "Users";

    /// <summary>Sub-folder of <see cref="UsersFolderName"/> that holds copied avatar images.</summary>
    public const string AvatarsFolderName = "avatars";

    public const string ProfileFileExtension = ".json";

    public const double MinAvatarZoom = 1.0;
    public const double MaxAvatarZoom = 5.0;

    /// <summary>Image extensions the avatar picker accepts.</summary>
    public static readonly string[] AllowedAvatarExtensions =
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    // Deliberately lenient — good enough to catch typos without rejecting valid addresses.
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A profile can be saved once it has a non-empty display name and a valid-or-empty e-mail.</summary>
    public static bool CanSave(string? displayName, string? email) =>
        !string.IsNullOrWhiteSpace(displayName) && IsEmailValidOrEmpty(email);

    public static bool IsEmailValidOrEmpty(string? email) =>
        string.IsNullOrWhiteSpace(email) || EmailRegex.IsMatch(email.Trim());

    public static string NormalizeUserId(string? userId)
    {
        var id = userId?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return DefaultUserId;
        }

        // Whitelist rather than blacklist: Path.GetInvalidFileNameChars() is platform-dependent (on
        // Linux/macOS it is only { '\0', '/' }), so it would let glob metacharacters (* ? [), path
        // separators and "../" through — which flow straight into file names and delete globs. Keep
        // only letters/digits plus '-' and '_' (covers GUIDs and "local-user"); everything else,
        // including '.', separators and glob chars, is dropped.
        var cleaned = new string(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray())
            .Trim('-', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? DefaultUserId : cleaned;
    }

    public static bool IsAllowedAvatarExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return AllowedAvatarExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds initials for the avatar placeholder from the best available name/email.
    /// Returns up to two upper-cased letters, or "?" when nothing usable is present.
    /// </summary>
    public static string BuildInitials(string? displayName, string? fullName, string? email)
    {
        var source = FirstNonEmpty(displayName, fullName, email);
        if (string.IsNullOrWhiteSpace(source))
        {
            return "?";
        }

        var beforeAt = source.Contains('@') ? source.Split('@')[0] : source;
        var words = beforeAt
            .Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return "?";
        }

        var first = char.ToUpperInvariant(words[0][0]);
        if (words.Length == 1)
        {
            return first.ToString();
        }

        var second = char.ToUpperInvariant(words[1][0]);
        return $"{first}{second}";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Clamps the avatar zoom into the supported range; non-finite / too-small values snap to 1.</summary>
    public static double ClampAvatarZoom(double zoom)
    {
        if (double.IsNaN(zoom) || double.IsInfinity(zoom))
        {
            return MinAvatarZoom;
        }

        return Math.Clamp(zoom, MinAvatarZoom, MaxAvatarZoom);
    }

    /// <summary>
    /// Clamps a pan offset (as a fraction of the circle diameter) so the image always covers the circle
    /// and no background shows. <paramref name="axisCoverRatio"/> is the image length along this axis
    /// divided by its shorter side (>= 1); at zoom 1 the shorter axis (ratio 1) cannot pan at all.
    /// </summary>
    public static double ClampAvatarOffset(double offset, double zoom, double axisCoverRatio)
    {
        if (double.IsNaN(offset) || double.IsInfinity(offset))
        {
            return 0;
        }

        var effectiveZoom = ClampAvatarZoom(zoom);
        var ratio = axisCoverRatio < 1 || double.IsNaN(axisCoverRatio) ? 1 : axisCoverRatio;
        var max = Math.Max(0, (ratio * effectiveZoom - 1) / 2);
        return Math.Clamp(offset, -max, max);
    }
}
