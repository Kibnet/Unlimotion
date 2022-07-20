using System.Threading.Tasks;

namespace Unlimotion.Interface
{
    public interface ICanOpenFileDialog
    {
        Task<string[]> Open();
    }
}
