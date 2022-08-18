namespace Unlimotion.ViewModel;

public class TaskStorageSettings
{
    public string Path { get; set; }

    public string URL { get; set; }

    public string Login { get; set; }

    //TODO стоит подумать над шифрованным хранением
    public string Password { get; set; }

    public bool IsServerMode { get ; set ; }
}