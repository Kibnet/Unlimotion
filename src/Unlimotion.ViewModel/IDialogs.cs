using System.Threading.Tasks;

namespace Unlimotion.ViewModel;

public interface IDialogs
{
    Task<string> ShowOpenFolderDialogAsync(string title = null, string directory = null);
}