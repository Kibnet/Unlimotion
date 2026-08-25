using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Vault;

namespace Unlimotion.ViewModel.Feed;

public sealed class FeedFilesDrawerViewModel : ReactiveObject, IDisposable
{
    private readonly INoteVault vault;
    private readonly DailyNoteNaming dailyNoteNaming;
    private readonly CancellationTokenSource lifetime = new();
    private bool isOpen;
    private bool isBusy;
    private string? errorMessage;
    private int disposed;

    public FeedFilesDrawerViewModel(INoteVault vault, DailyNoteNaming? dailyNoteNaming = null)
    {
        this.vault = vault ?? throw new ArgumentNullException(nameof(vault));
        this.dailyNoteNaming = dailyNoteNaming ?? DailyNoteNaming.Default;
        RefreshCommand = ReactiveCommand.CreateFromTask(() => RefreshAsync());
        OpenFileCommand = ReactiveCommand.CreateFromTask<FeedFileItemViewModel>(OpenFileAsync);
        CloseCommand = ReactiveCommand.Create(() => { IsOpen = false; });
    }

    public ObservableCollection<FeedFileItemViewModel> Files { get; } = new();

    public Func<string, Task>? OpenFileCallbackAsync { get; set; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<FeedFileItemViewModel, Unit> OpenFileCommand { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public bool IsOpen
    {
        get => isOpen;
        set => this.RaiseAndSetIfChanged(ref isOpen, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => this.RaiseAndSetIfChanged(ref isBusy, value);
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasFiles => Files.Count > 0;

    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>
    /// Refreshes the drawer using both its lifetime and the caller's operation
    /// lifetime. Candidate Feed sessions use this overload so a superseding
    /// root change can stop a pending filesystem read before it holds the
    /// reconfiguration gate.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        IsBusy = true;
        ErrorMessage = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.Token,
            cancellationToken);
        try
        {
            var paths = await vault.ListMarkdownFilesAsync(cancellation.Token).ConfigureAwait(true);
            var files = paths
                .Select(NormalizePath)
                .Where(path => IsThematicMarkdown(path, dailyNoteNaming))
                .Distinct(PathComparer)
                .OrderBy(static path => path, StringComparer.CurrentCultureIgnoreCase)
                .Select(static path => new FeedFileItemViewModel(path))
                .ToArray();

            Files.Clear();
            foreach (var file in files)
            {
                Files.Add(file);
            }

            this.RaisePropertyChanged(nameof(HasFiles));
        }
        catch (OperationCanceledException) when (
            lifetime.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenFileAsync(FeedFileItemViewModel? file)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (file is null || OpenFileCallbackAsync is null)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            await OpenFileCallbackAsync(file.RelativePath).ConfigureAwait(true);
            IsOpen = false;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        lifetime.Dispose();
        RefreshCommand.Dispose();
        OpenFileCommand.Dispose();
        CloseCommand.Dispose();
    }

    internal static bool IsThematicMarkdown(string path)
        => IsThematicMarkdown(path, DailyNoteNaming.Default);

    internal static bool IsThematicMarkdown(string path, DailyNoteNaming dailyNoteNaming)
    {
        ArgumentNullException.ThrowIfNull(dailyNoteNaming);
        if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0
               && !dailyNoteNaming.IsDailyRelativePath(path)
               && !parts.Contains(".unlimotion", StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public sealed record FeedFileItemViewModel(string RelativePath)
{
    public string Name => Path.GetFileNameWithoutExtension(RelativePath);

    public string Folder
    {
        get
        {
            var folder = Path.GetDirectoryName(RelativePath.Replace('/', Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(folder) ? string.Empty : folder.Replace('\\', '/');
        }
    }
}
