namespace Unlimotion.ViewModel
{
    public interface IFileTaskStorage
    {
        TaskItem? LoadFromFile(string filePath);
    }
}