using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Unlimotion.Test;

internal static class PlatformShellProjectContracts
{
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static async Task AssertAllPlatformShellProjectContractsAsync()
    {
        await AssertAndroidProjectIncludesSharedUiReferenceAndNativeGitAssetsAsync();
        await AssertBrowserProjectUsesSharedUiAndBrowserAppStartupContractAsync();
        await AssertIosProjectUsesSharedUiAndAvaloniaDelegateContractAsync();
    }

    public static async Task AssertAndroidProjectIncludesSharedUiReferenceAndNativeGitAssetsAsync()
    {
        var project = LoadProject("src/Unlimotion.Android/Unlimotion.Android.csproj");

        await Assert.That(GetProperty(project, "TargetFramework")).IsEqualTo("net10.0-android");
        await Assert.That(GetProperty(project, "RuntimeIdentifiers")!.Split(';')).Contains("android-arm64");
        await Assert.That(GetProperty(project, "RuntimeIdentifiers")!.Split(';')).Contains("android-x64");

        var projectReferences = GetIncludeValues(project, "ProjectReference");
        await Assert.That(projectReferences).Contains("../Unlimotion/Unlimotion.csproj");

        var packageReferences = GetIncludeValues(project, "PackageReference");
        await Assert.That(packageReferences).Contains("Avalonia.Android");
        await Assert.That(packageReferences).Contains("Xamarin.AndroidX.Core.SplashScreen");
        await Assert.That(packageReferences).Contains("LibGit2Sharp.NativeBinaries");

        var nativeBinariesReference = GetItem(project, "PackageReference", "LibGit2Sharp.NativeBinaries");
        await Assert.That(nativeBinariesReference.Attribute("GeneratePathProperty")?.Value).IsEqualTo("true");

        var nativeLibraries = GetIncludeValues(project, "AndroidNativeLibrary");
        foreach (var rid in new[] { "android-arm64", "android-x64" })
        {
            await Assert.That(nativeLibraries).Contains($"$(PkgLibGit2Sharp_NativeBinaries)/runtimes/{rid}/native/libcrypto.so.3");
            await Assert.That(nativeLibraries).Contains($"$(PkgLibGit2Sharp_NativeBinaries)/runtimes/{rid}/native/libcrypto.so");
            await Assert.That(nativeLibraries).Contains($"$(PkgLibGit2Sharp_NativeBinaries)/runtimes/{rid}/native/libssl.so.3");
            await Assert.That(nativeLibraries).Contains($"$(PkgLibGit2Sharp_NativeBinaries)/runtimes/{rid}/native/libssl.so");
            await Assert.That(nativeLibraries).Contains($"$(PkgLibGit2Sharp_NativeBinaries)/runtimes/{rid}/native/libssh2.so");
        }

        var mainActivity = ReadRepositoryFile("src/Unlimotion.Android/MainActivity.cs");
        await Assert.That(mainActivity).Contains("AvaloniaMainActivity");
        await Assert.That(mainActivity).Contains("ConfigureCoreAppServices");
        await Assert.That(mainActivity).Contains("App.ConfigureUpdateService");
        await Assert.That(mainActivity).Contains("Dialogs.PlatformOpenFolderDialogAsync");
        await Assert.That(mainActivity).Contains("TaskStorageFactory.PrepareFileStoragePathAsync");
    }

    public static async Task AssertBrowserProjectUsesSharedUiAndBrowserAppStartupContractAsync()
    {
        var project = LoadProject("src/Unlimotion.Browser/Unlimotion.Browser.csproj");

        await Assert.That(GetProperty(project, "TargetFramework")).IsEqualTo("net10.0-browser");
        await Assert.That(GetIncludeValues(project, "ProjectReference")).Contains("../Unlimotion/Unlimotion.csproj");
        await Assert.That(GetIncludeValues(project, "PackageReference")).Contains("Avalonia.Browser");

        var program = ReadRepositoryFile("src/Unlimotion.Browser/Program.cs");
        await Assert.That(program).Contains("BuildAvaloniaApp");
        await Assert.That(program).Contains("UseReactiveUI(App.ConfigureReactiveUIBuilder)");
        await Assert.That(program).Contains("StartBrowserAppAsync");
        await Assert.That(program).Contains("TaskStorageFactory.DefaultStoragePath");
        await Assert.That(program).Contains("MainControl.DialogsInstance");
        await Assert.That(program).Contains("AppBuilder.Configure<App>()");
    }

    public static async Task AssertIosProjectUsesSharedUiAndAvaloniaDelegateContractAsync()
    {
        var project = LoadProject("src/Unlimotion.iOS/Unlimotion.iOS.csproj");

        await Assert.That(GetProperty(project, "TargetFramework")).IsEqualTo("net10.0-ios");
        await Assert.That(GetIncludeValues(project, "ProjectReference")).Contains("../Unlimotion/Unlimotion.csproj");
        await Assert.That(GetIncludeValues(project, "PackageReference")).Contains("Avalonia.iOS");

        var appDelegate = ReadRepositoryFile("src/Unlimotion.iOS/AppDelegate.cs");
        await Assert.That(appDelegate).Contains("AvaloniaAppDelegate<App>");
        await Assert.That(appDelegate).Contains("UseReactiveUI(App.ConfigureReactiveUIBuilder)");

        var main = ReadRepositoryFile("src/Unlimotion.iOS/Main.cs");
        await Assert.That(main).Contains("UIApplication.Main(args, null, typeof(AppDelegate))");
    }

    public static string GetRepositoryPath(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Repository file was not found: {path}", path);
        }

        return path;
    }

    private static XDocument LoadProject(string relativePath)
    {
        return XDocument.Load(GetRepositoryPath(relativePath));
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(GetRepositoryPath(relativePath));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Unlimotion.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing src/Unlimotion.sln.");
    }

    private static string? GetProperty(XDocument project, string propertyName)
    {
        return project
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == propertyName)
            ?.Value
            .Trim();
    }

    private static IReadOnlySet<string> GetIncludeValues(XDocument project, string itemName)
    {
        return project
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => include!.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static XElement GetItem(XDocument project, string itemName, string include)
    {
        return project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == itemName &&
                string.Equals(element.Attribute("Include")?.Value, include, StringComparison.Ordinal));
    }
}
