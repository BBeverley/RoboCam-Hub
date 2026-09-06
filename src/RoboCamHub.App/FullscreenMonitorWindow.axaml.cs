using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RoboCamHub.Application;

namespace RoboCamHub.App;

public partial class FullscreenMonitorWindow : Window
{
    private bool _enteredFullscreen;

    public FullscreenMonitorWindow()
    {
        InitializeComponent();
        _enteredFullscreen = WindowState == WindowState.FullScreen;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += (_, _) => FullscreenRoot.Focus();
        Closed += OnClosed;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty)
        {
            return;
        }

        if (WindowState == WindowState.FullScreen)
        {
            _enteredFullscreen = true;
        }
        else if (_enteredFullscreen)
        {
            Close();
        }
    }

    public FullscreenMonitorWindow(WorkspaceViewModel workspace)
        : this()
    {
        DataContext = workspace;
        FullscreenPreviewHost.Preview = workspace.Preview;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is Key.Escape or Key.F11)
        {
            eventArgs.Handled = true;
            if (DataContext is WorkspaceViewModel workspace)
            {
                workspace.HandleEscape();
            }
            Close();
        }
    }

    private void OnExitFullscreen(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is WorkspaceViewModel workspace)
        {
            workspace.ExitFullscreen();
        }
        Close();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        FullscreenPreviewHost.DetachPreview();
    }
}
