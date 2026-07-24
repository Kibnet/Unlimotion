using System.Windows.Input;

namespace Unlimotion.AppAutomation.TestHost;

public sealed class AutomationAsyncCommand(Func<Task> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter) => await ExecuteAsync();

    public Task ExecuteAsync() => execute();
}

public sealed class AutomationAsyncCommand<T>(Func<T, Task> execute) : ICommand
    where T : class
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is T;

    public async void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            await ExecuteAsync(value);
        }
    }

    public Task ExecuteAsync(T parameter) => execute(parameter);
}
