using System.Windows.Input;

namespace PdfEditor.Infrastructure;

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

public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter)
    {
        if (parameter is T value) return canExecute?.Invoke(value) ?? true;
        if (parameter is null && default(T) is null) return canExecute?.Invoke(default!) ?? true;
        // Allow enum/int conversion from CommandParameter strings/boxed values
        try
        {
            if (parameter == null) return false;
            var converted = (T)Convert.ChangeType(parameter, typeof(T));
            return canExecute?.Invoke(converted) ?? true;
        }
        catch { return false; }
    }

    public void Execute(object? parameter)
    {
        if (parameter is T value) { execute(value); return; }
        if (typeof(T).IsEnum && parameter != null)
        {
            execute((T)Enum.Parse(typeof(T), parameter.ToString()!, ignoreCase: true));
            return;
        }
        if (parameter != null)
            execute((T)Convert.ChangeType(parameter, typeof(T)));
    }

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
