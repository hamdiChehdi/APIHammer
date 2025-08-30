using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace APIHammerUI.ViewModels;

/// <summary>
/// A command implementation that relays its execution logic to delegates
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute();
    }

    public void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}

/// <summary>
/// A generic command implementation that relays its execution logic to delegates
/// </summary>
public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter)
    {
        // Handle both typed and untyped parameters
        if (parameter is T typedParameter)
        {
            return _canExecute?.Invoke(typedParameter) ?? true;
        }
        
        // Handle null parameters for nullable types
        if (parameter == null && !typeof(T).IsValueType)
        {
            return _canExecute?.Invoke(default(T)) ?? true;
        }
        
        // Handle string parameters for string type
        if (typeof(T) == typeof(string) && parameter is string stringParameter)
        {
            return _canExecute?.Invoke((T)(object)stringParameter) ?? true;
        }

        return _canExecute?.Invoke(default(T)) ?? true;
    }

    public void Execute(object? parameter)
    {
        // Handle both typed and untyped parameters
        if (parameter is T typedParameter)
        {
            _execute(typedParameter);
            return;
        }
        
        // Handle null parameters for nullable types
        if (parameter == null && !typeof(T).IsValueType)
        {
            _execute(default(T));
            return;
        }
        
        // Handle string parameters for string type
        if (typeof(T) == typeof(string) && parameter is string stringParameter)
        {
            _execute((T)(object)stringParameter);
            return;
        }

        _execute(default(T));
    }

    public void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}

/// <summary>
/// An async command implementation that properly handles async operations without blocking the UI
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private volatile bool _isExecuting = false;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    public void Execute(object? parameter)
    {
        if (_isExecuting) return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        // Fire and forget - don't await this to prevent blocking UI
        _ = Task.Run(async () =>
        {
            try
            {
                // Execute the async operation on background thread
                await _execute().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Log the exception but don't let it bubble up to crash the UI
                System.Diagnostics.Debug.WriteLine($"AsyncRelayCommand exception: {ex}");
            }
            finally
            {
                _isExecuting = false;
                
                // Marshal back to UI thread for CanExecuteChanged
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        RaiseCanExecuteChanged();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        });
    }

    public void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}