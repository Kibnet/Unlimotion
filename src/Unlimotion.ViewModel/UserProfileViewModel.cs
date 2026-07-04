using System;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using PropertyChanged;
using ReactiveUI;
using Unlimotion.Domain;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion.ViewModel;

/// <summary>
/// Personal profile ("личный кабинет") view-model. Loads the current profile from
/// <see cref="IUserProfileStorage"/> on creation, exposes the editable fields, and saves them back as a
/// JSON file next to the tasks. The active profile id lives in configuration so the structure is ready
/// for multiple users, while today the app works with a single "my profile".
/// </summary>
[AddINotifyPropertyChangedInterface]
public class UserProfileViewModel
{
    private readonly IUserProfileStorage _storage;
    private readonly IConfiguration? _configuration;
    private readonly ILocalizationService _localization;

    private UserProfile _profile;

    public UserProfileViewModel(
        IUserProfileStorage storage,
        IConfiguration? configuration = null,
        ILocalizationService? localizationService = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _configuration = configuration;
        _localization = localizationService ?? LocalizationService.Current;

        CurrentUserId = ResolveCurrentUserId();
        _profile = new UserProfile { Id = CurrentUserId };

        // Storage Load is synchronous under the hood (Task.FromResult) — safe to unwrap here so the
        // fields are populated before the view binds.
        var loaded = _storage.Load(CurrentUserId).GetAwaiter().GetResult();
        ApplyProfile(loaded ?? _profile);

        SaveCommand = ReactiveCommand.CreateFromTask(
            SaveAsync,
            this.WhenAnyValue(x => x.DisplayName, x => x.Email,
                (name, email) => UserProfileSettings.CanSave(name, email)));

        PickAvatarCommand = ReactiveCommand.CreateFromTask(PickAvatarAsync);

        RemoveAvatarCommand = ReactiveCommand.CreateFromTask(
            RemoveAvatarAsync,
            this.WhenAnyValue(x => x.HasAvatar));
    }

    public IDialogs? Dialogs { get; set; }

    public string CurrentUserId { get; private set; }

    public string DisplayName { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? Birthday { get; set; }
    public string? AboutMe { get; set; }

    /// <summary>Avatar path relative to the storage root, as persisted. Null when no avatar is set.</summary>
    public string? AvatarRelativePath { get; set; }

    /// <summary>Circular-crop framing: zoom (>= 1) applied over a cover-fit of the avatar image.</summary>
    public double AvatarZoom { get; set; } = UserProfileSettings.MinAvatarZoom;

    /// <summary>Circular-crop pan along X, as a fraction of the circle diameter (0 = centered).</summary>
    public double AvatarOffsetX { get; set; }

    /// <summary>Circular-crop pan along Y, as a fraction of the circle diameter (0 = centered).</summary>
    public double AvatarOffsetY { get; set; }

    [DependsOn(nameof(AvatarRelativePath))]
    public string? AvatarAbsolutePath => _storage.ResolveAvatarAbsolutePath(AvatarRelativePath);

    [DependsOn(nameof(AvatarAbsolutePath))]
    public bool HasAvatar => AvatarAbsolutePath != null;

    [DependsOn(nameof(HasAvatar))]
    public bool HasNoAvatar => !HasAvatar;

    [DependsOn(nameof(DisplayName), nameof(FullName), nameof(Email))]
    public string Initials => UserProfileSettings.BuildInitials(DisplayName, FullName, Email);

    [DependsOn(nameof(Email))]
    public bool EmailIsValid => UserProfileSettings.IsEmailValidOrEmpty(Email);

    [DependsOn(nameof(DisplayName))]
    public bool DisplayNameMissing => string.IsNullOrWhiteSpace(DisplayName);

    [DependsOn(nameof(DisplayName), nameof(Email))]
    public bool CanSave => UserProfileSettings.CanSave(DisplayName, Email);

    /// <summary>Free-form status/validation feedback shown under the form.</summary>
    public string? Status { get; set; }

    public ICommand SaveCommand { get; }
    public ICommand PickAvatarCommand { get; }
    public ICommand RemoveAvatarCommand { get; }

    public async Task LoadAsync()
    {
        var loaded = await _storage.Load(CurrentUserId);
        ApplyProfile(loaded ?? new UserProfile { Id = CurrentUserId });
    }

    public async Task SaveAsync()
    {
        if (!CanSave)
        {
            Status = DisplayNameMissing
                ? _localization.Get("ProfileDisplayNameRequired")
                : _localization.Get("ProfileEmailInvalid");
            return;
        }

        _profile.Id = CurrentUserId;
        _profile.DisplayName = DisplayName.Trim();
        _profile.FullName = Trim(FullName);
        _profile.Email = Trim(Email);
        _profile.Birthday = Birthday;
        _profile.AboutMe = Trim(AboutMe);
        _profile.AvatarPath = AvatarRelativePath;
        _profile.AvatarZoom = AvatarZoom;
        _profile.AvatarOffsetX = AvatarOffsetX;
        _profile.AvatarOffsetY = AvatarOffsetY;

        try
        {
            _profile = await _storage.Save(_profile);
            Status = _localization.Get("ProfileSaved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = _localization.Format("ProfileSaveFailed", ex.Message);
        }
    }

    public async Task PickAvatarAsync()
    {
        if (Dialogs == null)
        {
            return;
        }

        var selected = await Dialogs.ShowOpenFileDialogAsync(
            _localization.Get("ProfilePickAvatarTitle"),
            UserProfileSettings.AllowedAvatarExtensions);

        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        if (!UserProfileSettings.IsAllowedAvatarExtension(selected))
        {
            Status = _localization.Get("ProfileAvatarUnsupported");
            return;
        }

        var previousAvatar = AvatarRelativePath;
        var previousZoom = AvatarZoom;
        var previousOffsetX = AvatarOffsetX;
        var previousOffsetY = AvatarOffsetY;
        try
        {
            AvatarRelativePath = await _storage.ImportAvatar(CurrentUserId, selected);
            // A fresh image starts centered at 1x — the user reframes from there.
            ResetAvatarFraming();
            _profile.AvatarPath = AvatarRelativePath;
            _profile.AvatarZoom = AvatarZoom;
            _profile.AvatarOffsetX = AvatarOffsetX;
            _profile.AvatarOffsetY = AvatarOffsetY;
            _profile = await _storage.Save(_profile);
            Status = _localization.Get("ProfileAvatarUpdated");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            // Roll back so the shown avatar matches what is actually persisted.
            AvatarRelativePath = previousAvatar;
            AvatarZoom = previousZoom;
            AvatarOffsetX = previousOffsetX;
            AvatarOffsetY = previousOffsetY;
            _profile.AvatarPath = previousAvatar;
            Status = _localization.Format("ProfileAvatarImportFailed", ex.Message);
        }
    }

    public async Task RemoveAvatarAsync()
    {
        if (!HasAvatar)
        {
            return;
        }

        var previousAvatar = AvatarRelativePath;
        AvatarRelativePath = null;
        ResetAvatarFraming();
        _profile.AvatarPath = null;
        _profile.AvatarZoom = AvatarZoom;
        _profile.AvatarOffsetX = AvatarOffsetX;
        _profile.AvatarOffsetY = AvatarOffsetY;
        try
        {
            _profile = await _storage.Save(_profile);
            Status = _localization.Get("ProfileAvatarRemoved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AvatarRelativePath = previousAvatar;
            _profile.AvatarPath = previousAvatar;
            Status = _localization.Format("ProfileSaveFailed", ex.Message);
        }
    }

    /// <summary>Resets the circular-crop framing to centered, 1x.</summary>
    public void ResetAvatarFraming()
    {
        AvatarZoom = UserProfileSettings.MinAvatarZoom;
        AvatarOffsetX = 0;
        AvatarOffsetY = 0;
    }

    private void ApplyProfile(UserProfile profile)
    {
        _profile = profile;
        CurrentUserId = string.IsNullOrWhiteSpace(profile.Id) ? CurrentUserId : profile.Id;
        DisplayName = profile.DisplayName ?? string.Empty;
        FullName = profile.FullName;
        Email = profile.Email;
        Birthday = profile.Birthday;
        AboutMe = profile.AboutMe;
        AvatarRelativePath = profile.AvatarPath;
        // Zoom is clamped (older profiles stored 0); offsets were already valid when saved.
        AvatarZoom = UserProfileSettings.ClampAvatarZoom(profile.AvatarZoom);
        AvatarOffsetX = profile.AvatarOffsetX;
        AvatarOffsetY = profile.AvatarOffsetY;
    }

    private string ResolveCurrentUserId()
    {
        var section = _configuration?.GetSection(UserProfileSettings.SectionName)
            .GetSection(UserProfileSettings.CurrentUserIdKey);
        var configured = section?.Get<string>();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return UserProfileSettings.NormalizeUserId(configured);
        }

        var normalized = UserProfileSettings.DefaultUserId;
        section?.Set(normalized);
        return normalized;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
