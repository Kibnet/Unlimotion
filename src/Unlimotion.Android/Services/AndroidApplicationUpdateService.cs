using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using AndroidX.Core.Content;
using Unlimotion.ViewModel;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Android.Services;

public sealed class AndroidApplicationUpdateService : IApplicationUpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Kibnet/Unlimotion/releases/latest";
    private const string ApkMimeType = "application/vnd.android.package-archive";
    private const string FileProviderAuthority = "com.Kibnet.Unlimotion.fileprovider";
    private const string PendingMetadataFileName = "pending-update.json";

    private readonly Activity _activity;
    private readonly HttpClient _httpClient;
    private readonly string? _targetRuntimeIdentifier;
    private AndroidUpdateRelease? _lastRelease;

    public AndroidApplicationUpdateService(Activity activity)
        : this(activity, new HttpClient())
    {
    }

    internal AndroidApplicationUpdateService(Activity activity, HttpClient httpClient)
    {
        _activity = activity;
        _httpClient = httpClient;
        _targetRuntimeIdentifier = ResolveTargetRuntimeIdentifier();

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Unlimotion.Android");
        }
    }

    public bool IsSupported => !string.IsNullOrWhiteSpace(_targetRuntimeIdentifier);

    public string CurrentVersion => GetPackageVersionName();

    public ApplicationUpdateInfo? PendingUpdate => TryReadDownloadedUpdate()?.ToApplicationUpdateInfo();

    public async Task<ApplicationUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();

        using var response = await _httpClient.GetAsync(
            LatestReleaseApiUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var release = AndroidUpdateRelease.TryCreate(document.RootElement, _targetRuntimeIdentifier!);
        if (release == null || !IsNewerVersion(release.Version, CurrentVersion))
        {
            _lastRelease = null;
            return null;
        }

        _lastRelease = release;
        return release.ToApplicationUpdateInfo();
    }

    public async Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();

        var release = _lastRelease;
        if (release == null)
        {
            await CheckForUpdatesAsync(cancellationToken);
            release = _lastRelease;
        }

        if (release == null)
        {
            throw new InvalidOperationException("No Android update was selected for download.");
        }

        var updatesDirectory = EnsureUpdatesDirectory();
        DeleteStaleUpdateFiles(updatesDirectory);

        var fileName = $"Unlimotion-{release.Version}-{_targetRuntimeIdentifier}.apk";
        var apkPath = Path.Combine(updatesDirectory, fileName);
        using var response = await _httpClient.GetAsync(
            release.AssetDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(apkPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        var downloadedUpdate = new DownloadedAndroidUpdate(
            release.Version,
            release.ReleaseNotes,
            fileName);
        await File.WriteAllTextAsync(
            GetPendingMetadataPath(updatesDirectory),
            JsonSerializer.Serialize(downloadedUpdate),
            cancellationToken);
    }

    public void ApplyUpdateAndRestart()
    {
        EnsureSupported();

        var downloadedUpdate = TryReadDownloadedUpdate()
                               ?? throw new InvalidOperationException("No downloaded Android update is ready to install.");

        if (OperatingSystem.IsAndroidVersionAtLeast(26) &&
            _activity.PackageManager?.CanRequestPackageInstalls() == false)
        {
            var settingsIntent = new Intent(
                Settings.ActionManageUnknownAppSources,
                global::Android.Net.Uri.Parse($"package:{_activity.PackageName}"));
            settingsIntent.AddFlags(ActivityFlags.NewTask);
            _activity.StartActivity(settingsIntent);
            throw new ApplicationUpdateUserActionRequiredException(
                L10n.Get("UpdateInstallPermissionRequired"));
        }

        var apkFile = new Java.IO.File(downloadedUpdate.FilePath);
        var apkUri = FileProvider.GetUriForFile(
            _activity,
            FileProviderAuthority,
            apkFile);

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(apkUri, ApkMimeType);
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        _activity.StartActivity(intent);
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException("Android updates are only available for android-arm64 and android-x64 packages.");
        }
    }

    private string GetPackageVersionName()
    {
        try
        {
#pragma warning disable CS0618
            var packageInfo = _activity.PackageManager?.GetPackageInfo(_activity.PackageName!, 0);
#pragma warning restore CS0618
            if (!string.IsNullOrWhiteSpace(packageInfo?.VersionName))
            {
                return packageInfo.VersionName!;
            }
        }
        catch
        {
        }

        return typeof(AndroidApplicationUpdateService).Assembly.GetName().Version?.ToString() ?? string.Empty;
    }

    private DownloadedAndroidUpdate? TryReadDownloadedUpdate()
    {
        try
        {
            var updatesDirectory = EnsureUpdatesDirectory();
            var metadataPath = GetPendingMetadataPath(updatesDirectory);
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            var metadata = JsonSerializer.Deserialize<DownloadedAndroidUpdate>(File.ReadAllText(metadataPath));
            if (metadata == null ||
                string.IsNullOrWhiteSpace(metadata.FileName) ||
                !IsNewerVersion(metadata.Version, CurrentVersion))
            {
                DeleteStaleUpdateFiles(updatesDirectory);
                return null;
            }

            var apkPath = Path.Combine(updatesDirectory, metadata.FileName);
            if (!File.Exists(apkPath))
            {
                DeleteStaleUpdateFiles(updatesDirectory);
                return null;
            }

            return metadata with { FilePath = apkPath };
        }
        catch
        {
            return null;
        }
    }

    private string EnsureUpdatesDirectory()
    {
        var cacheDirectory = _activity.CacheDir?.AbsolutePath
                             ?? _activity.ExternalCacheDir?.AbsolutePath
                             ?? _activity.FilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new InvalidOperationException("Could not resolve Android update cache directory.");
        }

        var updatesDirectory = Path.Combine(cacheDirectory, "updates");
        Directory.CreateDirectory(updatesDirectory);
        return updatesDirectory;
    }

    private static string GetPendingMetadataPath(string updatesDirectory) =>
        Path.Combine(updatesDirectory, PendingMetadataFileName);

    private static void DeleteStaleUpdateFiles(string updatesDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(updatesDirectory, "*.apk"))
        {
            File.Delete(file);
        }

        var metadataPath = GetPendingMetadataPath(updatesDirectory);
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }
    }

    private static string? ResolveTargetRuntimeIdentifier()
    {
        foreach (var abi in Build.SupportedAbis ?? Array.Empty<string>())
        {
            var rid = abi switch
            {
                "arm64-v8a" => "android-arm64",
                "x86_64" => "android-x64",
                _ => null
            };

            if (rid != null)
            {
                return rid;
            }
        }

#pragma warning disable CS0618
        return Build.CpuAbi switch
        {
            "arm64-v8a" => "android-arm64",
            "x86_64" => "android-x64",
            _ => null
        };
#pragma warning restore CS0618
    }

    private static bool IsNewerVersion(string candidateVersion, string currentVersion)
    {
        var candidate = TryParseVersion(candidateVersion);
        var current = TryParseVersion(currentVersion);
        return candidate != null && current != null && candidate.CompareTo(current) > 0;
    }

    private static Version? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var versionText = new string(normalized
            .TakeWhile(c => char.IsDigit(c) || c == '.')
            .ToArray());
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return null;
        }

        var parts = versionText
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var number) ? number : (int?)null)
            .ToArray();
        if (parts.Length == 0 || parts.Any(part => part == null))
        {
            return null;
        }

        return new Version(
            parts.ElementAtOrDefault(0) ?? 0,
            parts.ElementAtOrDefault(1) ?? 0,
            parts.ElementAtOrDefault(2) ?? 0,
            parts.ElementAtOrDefault(3) ?? 0);
    }

    private sealed record AndroidUpdateRelease(
        string Version,
        string? ReleaseNotes,
        string AssetDownloadUrl)
    {
        public ApplicationUpdateInfo ToApplicationUpdateInfo() =>
            new(Version, ReleaseNotes);

        public static AndroidUpdateRelease? TryCreate(JsonElement release, string targetRuntimeIdentifier)
        {
            var version = GetString(release, "tag_name") ?? GetString(release, "name");
            if (string.IsNullOrWhiteSpace(version) ||
                !release.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = GetString(asset, "name");
                if (string.IsNullOrWhiteSpace(assetName) ||
                    !assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
                    !assetName.Contains(targetRuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var downloadUrl = GetString(asset, "browser_download_url");
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    continue;
                }

                return new AndroidUpdateRelease(
                    version.TrimStart('v', 'V'),
                    GetString(release, "body"),
                    downloadUrl);
            }

            return null;
        }

        private static string? GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private sealed record DownloadedAndroidUpdate(
        string Version,
        string? ReleaseNotes,
        string FileName)
    {
        public string FilePath { get; init; } = string.Empty;

        public ApplicationUpdateInfo ToApplicationUpdateInfo() =>
            new(Version, ReleaseNotes);
    }
}
