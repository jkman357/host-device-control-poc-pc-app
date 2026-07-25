// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HostDeviceControl.App.Infrastructure;

/// <summary>
/// Serializes one asynchronous UI command invocation and owns the task created
/// by the synchronous <see cref="ICommand.Execute"/> framework callback.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception> _unexpectedExceptionHandler;
    private Task? _executionTask;
    private bool _isExecuting;

    public AsyncRelayCommand(
        Func<Task> executeAsync,
        Action<Exception> unexpectedExceptionHandler,
        Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync ??
            throw new ArgumentNullException(nameof(executeAsync));
        _unexpectedExceptionHandler = unexpectedExceptionHandler ??
            throw new ArgumentNullException(nameof(unexpectedExceptionHandler));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting &&
        ((_executionTask is null) || _executionTask.IsCompleted) &&
        (_canExecute?.Invoke() ?? true);

    /// <summary>
    /// Framework callback. The command stores and observes its owned task; no
    /// exception is abandoned to the finalizer or dispatcher.
    /// </summary>
    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _executionTask = ExecuteAndObserveAsync(parameter);
    }

    /// <summary>
    /// Testable task-returning command implementation.
    /// </summary>
    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private async Task ExecuteAndObserveAsync(object? parameter)
    {
        try
        {
            await ExecuteAsync(parameter);
        }
        catch (OperationCanceledException)
        {
            // User cancellation and application shutdown are expected outcomes.
        }
        catch (Exception exception)
        {
            _unexpectedExceptionHandler(exception);
        }
        finally
        {
            _executionTask = null;
            RaiseCanExecuteChanged();
        }
    }
}
