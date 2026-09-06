using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoboCamHub.Application;
using RoboCamHub.Domain;

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

    private async void OnAddCamera(object? sender, RoutedEventArgs eventArgs)
    {
        if (_workspace is null)
        {
            return;
        }
        var camera = await new CameraElementPickerWindow(_workspace.Cameras).ShowDialog<CameraItemViewModel?>(this);
        if (camera is not null)
        {
            await _workspace.SelectedView.Editor.AddCameraAsync(camera.Definition.Id);
            EditorCanvas.Focus();
        }
    }

    private async void OnAddText(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { } editor)
        {
            await editor.AddTextAsync();
            EditorCanvas.Focus();
        }
    }

    private async void OnAddRectangle(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { } editor)
        {
            await editor.AddRectangleAsync();
            EditorCanvas.Focus();
        }
    }

    private async void OnAddFrame(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { } editor)
        {
            await editor.AddFrameAsync();
            EditorCanvas.Focus();
        }
    }

    private async void OnAddImage(object? sender, RoutedEventArgs eventArgs)
    {
        var editor = EditorCanvas.Editor;
        if (editor is null)
        {
            return;
        }
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import image asset",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("PNG or JPEG image")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg"],
                        MimeTypes = ["image/png", "image/jpeg"],
                    },
                ],
            });
            var file = files.FirstOrDefault();
            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            var extension = Path.GetExtension(path);
            var mediaType = string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                ? AssetMediaType.Png
                : AssetMediaType.Jpeg;
            var dimensions = ImageAssetMetadata.ReadDimensions(path, mediaType);
            var asset = new AssetDefinition(
                $"asset-{Guid.NewGuid():N}",
                Path.GetFileName(path),
                mediaType,
                path,
                dimensions.Width,
                dimensions.Height);
            await editor.AddImageAsync(asset);
            EditorCanvas.Focus();
        }
        catch (Exception exception)
        {
            editor.ReportOperatorError("import image", exception);
        }
    }

    private async void OnCreateView(object? sender, RoutedEventArgs eventArgs)
    {
        var workspace = _workspace;
        if (workspace is null)
        {
            return;
        }

        var draft = ViewCreationViewModel.Create(workspace.Cameras);
        var definition = await new ViewCreationWindow(draft).ShowDialog<ViewDefinition?>(this);
        if (definition is not null)
        {
            await workspace.CreateViewAsync(definition);
        }
    }

    private async void OnDuplicateView(object? sender, RoutedEventArgs eventArgs)
    {
        var workspace = _workspace;
        if (workspace is null)
        {
            return;
        }

        var draft = ViewCreationViewModel.Duplicate(workspace.Cameras, workspace.SelectedView.Definition);
        var definition = await new ViewCreationWindow(draft).ShowDialog<ViewDefinition?>(this);
        if (definition is not null)
        {
            await workspace.CreateViewAsync(definition);
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
        if (editor?.SelectedElement?.Definition is CameraElementDefinition)
        {
            if (editor.BeginProperties() is not null)
            {
                _ = new CameraElementPropertiesWindow(editor).ShowDialog(this);
            }
            return;
        }
        if (editor?.BeginVisualProperties() is null)
        {
            return;
        }
        _ = new VisualElementPropertiesWindow(editor).ShowDialog(this);
    }

    private void OnLocateSourceRequested(object? sender, string cameraId)
        => _workspace?.LocateCamera(cameraId);
}
