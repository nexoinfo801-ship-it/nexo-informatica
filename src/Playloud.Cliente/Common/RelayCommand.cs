using System.Windows.Input;

namespace Playloud.Cliente.Common;

public sealed class RelayCommand(
    Action<object?> execute,
    Predicate<object?>? canExecute = null) : ICommand
{
    private readonly Action<object?> _execute =
        execute ?? throw new ArgumentNullException(nameof(execute));

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}

public sealed class AsyncRelayCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null) : ICommand
{
    private readonly Func<Task> _execute =
        execute ?? throw new ArgumentNullException(nameof(execute));
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
