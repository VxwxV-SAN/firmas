using System.Windows.Input;

namespace FirmasPDF.Infrastructure;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public sealed class AsyncRelayCommand(Func<Task> execute, Action<Exception> onError,
    Func<bool>? canExecute = null) : ICommand
{
    private bool _executing;
    public bool CanExecute(object? parameter) => !_executing && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _executing = true;
        CommandManager.InvalidateRequerySuggested();
        try { await execute(); }
        catch (Exception ex) { onError(ex); }
        finally
        {
            _executing = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
