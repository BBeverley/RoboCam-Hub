using Avalonia.Controls;
using RoboCamHub.Application;

namespace RoboCamHub.App;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private WorkspaceViewModel? _workspace;
    private bool _allowClose;
    private bool _shutdownStarted;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        try
        {
            var runtime = await WorkspaceRuntimeService.CreateDefaultAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                await runtime.DisposeAsync();
                return;
            }

            _workspace = new WorkspaceViewModel(runtime, new AvaloniaUiDispatcher());
            DataContext = _workspace;
            ViewPreviewHost.Preview = _workspace.Preview;
            StartupPanel.IsVisible = false;
            WorkspaceRoot.IsVisible = true;
            _workspace.StartStatusPolling();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StartupText.Text = $"Runtime startup failed: {exception.Message}";
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_allowClose)
        {
            return;
        }

        _lifetime.Cancel();
        if (_workspace is null)
        {
            _allowClose = true;
            return;
        }

        eventArgs.Cancel = true;
        if (!_shutdownStarted)
        {
            _shutdownStarted = true;
            _ = DisposeAndCloseAsync();
        }
    }

    private async Task DisposeAndCloseAsync()
    {
        ViewPreviewHost.DetachPreview();
        await _workspace!.DisposeAsync();
        _workspace = null;
        DataContext = null;
        _allowClose = true;
        Close();
        _lifetime.Dispose();
    }
}
