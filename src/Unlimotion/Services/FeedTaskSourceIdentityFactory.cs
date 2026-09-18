using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unlimotion.Notes.Operations;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public static class FeedTaskSourceIdentityFactory
{
    public static FeedTaskSourceIdentity? Capture(ITaskSourceManager? manager, ITaskStorage? boundStorage)
    {
        var runtime = manager?.ActiveSource;
        if (runtime is null || boundStorage is null || !ReferenceEquals(runtime.Storage, boundStorage)) return null;
        var source = runtime.Descriptor;
        string binding;
        if (source.Kind == TaskSourceKind.File)
        {
            if (string.IsNullOrWhiteSpace(source.Path)) return null;
            var path = Path.GetFullPath(source.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
            binding = "file\n" + path;
        }
        else
        {
            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)) return null;
            var account = runtime.ServerSettings?.UserId;
            if (string.IsNullOrWhiteSpace(account)) account = runtime.ServerSettings?.Login;
            if (string.IsNullOrWhiteSpace(account)) return null;
            // Hash identity only; never persist tokens, credentials or the raw account identifier in the journal.
            binding = "server\n" + uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/')
                + "\n" + account;
        }
        return new(source.Id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding))));
    }
}
