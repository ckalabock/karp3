using System.Windows;
using HttpMonitorApp.ViewModels;

namespace HttpMonitorApp;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Closing -= OnClosing;
        await _viewModel.DisposeAsync();
    }
}
