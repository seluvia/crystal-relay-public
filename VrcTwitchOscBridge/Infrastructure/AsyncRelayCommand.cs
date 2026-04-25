using System.Windows.Input;

namespace VrcTwitchOscBridge.Infrastructure;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> executeAsync;
    private readonly Func<bool>? canExecute;
    private bool isRunning;

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        this.executeAsync = executeAsync;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isRunning && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            isRunning = true;
            NotifyCanExecuteChanged();
            await executeAsync();
        }
        finally
        {
            isRunning = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
