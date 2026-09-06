using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
        EditorCanvas.PropertiesRequested += OnEditorPropertiesRequested;
        EditorCanvas.LocateSourceRequested += OnLocateSourceRequested;
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
            _workspace.PropertyChanged += OnWorkspacePropertyChanged;
            DataContext = _workspace;
            ViewPreviewHost.Preview = _workspace.Preview;
            EditorCanvas.Editor = _workspace.SelectedView.Editor;
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
        EditorCanvas.Editor = null;
        _workspace!.PropertyChanged -= OnWorkspacePropertyChanged;
        await _workspace!.DisposeAsync();
        _workspace = null;
        DataContext = null;
        _allowClose = true;
        Close();
        _lifetime.Dispose();
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(WorkspaceViewModel.SelectedView) && _workspace is not null)
        {
            EditorCanvas.Editor = _workspace.SelectedView.Editor;
        }
    }

    private async void OnAddCameraToView(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { DataContext: CameraItemViewModel camera } && _workspace is not null)
        {
            await _workspace.SelectedView.Editor.AddCameraAsync(camera.Definition.Id);
            EditorCanvas.Focus();
        }
    }

    private void OnEditorElementSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is ComboBox { SelectedItem: ViewEditorElementViewModel element }
            && EditorCanvas.Editor is { } editor)
        {
            editor.SelectElement(element.Id);
        }
    }

    private async void OnDuplicate(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { } editor)
        {
            await editor.DuplicateSelectedAsync();
        }
    }

    private async void OnDeleteElement(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { } editor)
        {
            await editor.DeleteSelectedAsync();
        }
    }

    private async void OnBringForward(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { } editor)
        {
            await editor.BringForwardAsync();
        }
    }

    private async void OnSendBackward(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { } editor)
        {
            await editor.SendBackwardAsync();
        }
    }

    private void OnProperties(object? sender, RoutedEventArgs eventArgs) => OpenProperties();

    private void OnEditorPropertiesRequested(object? sender, EventArgs eventArgs) => OpenProperties();

    private void OpenProperties()
    {
        var editor = EditorCanvas.Editor;
        if (editor?.BeginProperties() is null)
        {
            return;
        }

        _ = new CameraElementPropertiesWindow(editor).ShowDialog(this);
    }

    private void OnLocateSourceRequested(object? sender, string cameraId)
        => _workspace?.LocateCamera(cameraId);
}
