using System;
using System.Windows;
using HostDeviceControl.App.ViewModels;

namespace HostDeviceControl.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Closed += OnClosed;
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        await _viewModel.DisposeAsync();
    }
}
