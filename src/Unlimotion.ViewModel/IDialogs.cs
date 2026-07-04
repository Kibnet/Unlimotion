using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;

public interface IDialogs
{
    Task<string> ShowOpenFolderDialogAsync(string? title = null, string? directory = null);

    /// <summary>
    /// Shows a single-file open dialog and returns the chosen local path, or an empty string when the
    /// user cancels. <paramref name="allowedExtensions"/> are dotted (e.g. ".png") and optional.
    /// </summary>
    Task<string> ShowOpenFileDialogAsync(
        string? title = null,
        IReadOnlyList<string>? allowedExtensions = null,
        string? directory = null);
}
