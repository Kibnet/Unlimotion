using System;

namespace Unlimotion.Domain
{
    /// <summary>
    /// Local, file-backed personal profile ("личный кабинет"). Stored as a JSON file next to the
    /// tasks (in a <c>Users</c> sub-folder) so it travels with the same git backup, yet is invisible
    /// to the task scan. Designed so several profiles can live side by side for future multi-user
    /// support; today the app works with a single current profile.
    /// </summary>
    public record UserProfile
    {
        /// <summary>Stable identifier and file name (without extension). Also used as the task author id.</summary>
        public string Id { get; set; } = null!;

        /// <summary>Short name shown next to the avatar. The only field required to have a value.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Optional full/legal name.</summary>
        public string? FullName { get; set; }

        /// <summary>Optional e-mail address.</summary>
        public string? Email { get; set; }

        /// <summary>Optional date of birth (time part is ignored).</summary>
        public DateTimeOffset? Birthday { get; set; }

        /// <summary>Optional free-form "about me" text.</summary>
        public string? AboutMe { get; set; }

        /// <summary>
        /// Avatar image path, stored relative to the storage root so the profile file stays portable
        /// and git-friendly, e.g. <c>Users/avatars/local-user-1720000000.png</c>. Null when unset.
        /// </summary>
        public string? AvatarPath { get; set; }

        /// <summary>
        /// Circular-crop framing of the avatar: which zone of the image is shown in the thumbnail.
        /// Non-destructive — the original image is kept and these describe zoom/pan applied on top of a
        /// cover-fit. <see cref="AvatarZoom"/> is a scale (1 = whole cover-fit, larger = zoomed in);
        /// offsets are pan as a fraction of the circle diameter (0 = centered).
        /// </summary>
        public double AvatarZoom { get; set; } = 1.0;

        public double AvatarOffsetX { get; set; }

        public double AvatarOffsetY { get; set; }

        public DateTimeOffset CreatedDateTime { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? UpdatedDateTime { get; set; }

        public override string ToString() => $"{Id};{DisplayName}";
    }
}
