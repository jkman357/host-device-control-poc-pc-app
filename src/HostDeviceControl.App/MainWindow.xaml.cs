// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using HostDeviceControl.App.ViewModels;

namespace HostDeviceControl.App;

/// <summary>
/// Hosts the single-device PoC view and owns orderly UI shutdown.
/// </summary>
public partial class MainWindow : Window, IAsyncDisposable
{
    private readonly MainViewModel _viewModel;
    private bool _shutdownStarted;
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(Dispatcher);
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    /// <summary>
    /// Releases the view model and all session-owned resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_shutdownComplete)
        {
            return;
        }

        await _viewModel.DisposeAsync();
    }

    // Framework-required async event boundary. All exceptions are handled here.
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;

        try
        {
            await DisposeAsync();
            _shutdownComplete = true;
            Closing -= OnClosing;
            Close();
        }
        catch (Exception exception)
        {
            _shutdownStarted = false;
            MessageBox.Show(
                $"The application could not complete shutdown cleanly.\n\n" +
                exception.Message,
                "Shutdown incomplete",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
