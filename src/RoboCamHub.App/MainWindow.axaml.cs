using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using RoboCamHub.Application;
using RoboCamHub.Domain;
using RoboCamHub.Persistence;

namespace RoboCamHub.App;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AvaloniaUiDispatcher _dispatcher = new();
    private readonly ShowFileService _showFiles;
    private readonly RecoveryStore _recovery;
    private readonly MachinePreferencesStore _preferencesStore;
    private readonly WorkspaceLoadCoordinator _workspaceLoader;
    private WorkspaceViewModel? _workspace;
    private PreparedWorkspace? _preparedWorkspace;
    private WorkspacePersistenceCoordinator? _persistence;
    private MachinePreferences _preferences = new();
    private bool _allowClose;
    private bool _shutdownStarted;
    private bool _fileOperationInProgress;
    private Window? _propertiesWindow;
    private FullscreenMonitorWindow? _fullscreenWindow;

    public MainWindow()
    {
        _showFiles = new ShowFileService();
        _recovery = new RecoveryStore(_showFiles);
        _preferencesStore = new MachinePreferencesStore();
        _workspaceLoader = new WorkspaceLoadCoordinator(
            _showFiles,
            _recovery,
            new DefaultWorkspaceRuntimeFactory(),
            _dispatcher);
        InitializeComponent();
        EditorCanvas.PropertiesRequested += OnEditorPropertiesRequested;
        EditorCanvas.LocateSourceRequested += OnLocateSourceRequested;
        Opened += OnOpened;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        try
        {
            try
            {
                _preferences = await _preferencesStore.LoadAsync(_lifetime.Token);
                ApplyMachinePreferences();
            }
            catch (Exception exception)
            {
                StartupText.Text = $"Machine preferences were ignored: {exception.Message}";
            }

            var prepared = await _workspaceLoader.NewAsync(_lifetime.Token);
            await ActivatePreparedWorkspaceAsync(prepared);
            try
            {
                var recovery = (await _recovery.FindNewerAsync(_lifetime.Token)).FirstOrDefault();
                if (recovery is not null)
                {
                    var decision = await new RecoveryPromptWindow(recovery).ShowDialog<RecoveryDecision>(this);
                    if (decision == RecoveryDecision.Recover)
                    {
                        var recovered = await _workspaceLoader.RecoverAsync(recovery, _lifetime.Token);
                        await ActivatePreparedWorkspaceAsync(recovered);
                    }
                    else if (decision == RecoveryDecision.Discard)
                    {
                        await _recovery.DiscardAsync(recovery);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _workspace!.ReportPersistenceErrorAsync("recovery", exception);
            }

            StartupPanel.IsVisible = false;
            WorkspaceRoot.IsVisible = true;
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

        if (_workspace is null)
        {
            _allowClose = true;
            return;
        }

        eventArgs.Cancel = true;
        if (!_shutdownStarted)
        {
            _shutdownStarted = true;
            _ = ConfirmAndCloseAsync();
        }
    }

    private async Task ConfirmAndCloseAsync()
    {
        if (!await ConfirmSafeToReplaceAsync())
        {
            _shutdownStarted = false;
            return;
        }
        _lifetime.Cancel();
        await DisposeAndCloseAsync();
    }

    private async Task DisposeAndCloseAsync()
    {
        _propertiesWindow?.Close();
        _propertiesWindow = null;
        _fullscreenWindow?.Close();
        _fullscreenWindow = null;
        ViewPreviewHost.DetachPreview();
        EditorCanvas.Editor = null;
        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        }
        if (_persistence is not null)
        {
            await _persistence.DisposeAsync();
            _persistence = null;
        }
        if (_preparedWorkspace is not null)
        {
            await _preparedWorkspace.DisposeAsync();
            _preparedWorkspace = null;
        }
        await SaveMachinePreferencesAsync();
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
        if (eventArgs.PropertyName == nameof(WorkspaceViewModel.Mode))
        {
            UpdateModePresentation();
        }
        if (eventArgs.PropertyName is nameof(WorkspaceViewModel.WindowTitle)
            or nameof(WorkspaceViewModel.IsDirty)
            or nameof(WorkspaceViewModel.CurrentFilePath))
        {
            UpdateWindowTitle();
        }
    }

    private async void OnAddCameraToView(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { DataContext: CameraItemViewModel camera }
            && _workspace is { CanEditScene: true })
        {
            await _workspace.SelectedView.Editor.AddCameraAsync(camera.Definition.Id);
            EditorCanvas.Focus();
        }
    }

    private async void OnAddCamera(object? sender, RoutedEventArgs eventArgs)
    {
        if (_workspace is not { CanEditScene: true })
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
        if (EditorCanvas.Editor is { CanEditScene: true } editor)
        {
            await editor.AddTextAsync();
            EditorCanvas.Focus();
        }
    }

    private async void OnAddRectangle(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { CanEditScene: true } editor)
        {
            await editor.AddRectangleAsync();
            EditorCanvas.Focus();
        }
    }

    private async void OnAddFrame(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { CanEditScene: true } editor)
        {
            await editor.AddFrameAsync();
            EditorCanvas.Focus();
        }
    }

    private async void OnAddImage(object? sender, RoutedEventArgs eventArgs)
    {
        var editor = EditorCanvas.Editor;
        if (editor is not { CanEditScene: true })
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
        if (workspace is not { CanCreateView: true })
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
        if (workspace is not { CanCreateView: true })
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
            && EditorCanvas.Editor is { CanEditScene: true } editor)
        {
            editor.SelectElement(element.Id);
        }
    }

    private async void OnDuplicate(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { CanEditScene: true } editor)
        {
            await editor.DuplicateSelectedAsync();
        }
    }

    private async void OnDeleteElement(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { CanEditScene: true } editor)
        {
            await editor.DeleteSelectedAsync();
        }
    }

    private async void OnBringForward(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { CanEditScene: true } editor)
        {
            await editor.BringForwardAsync();
        }
    }

    private async void OnSendBackward(object? sender, RoutedEventArgs eventArgs)
    {
        if (EditorCanvas.Editor is { CanEditScene: true } editor)
        {
            await editor.SendBackwardAsync();
        }
    }

    private void OnProperties(object? sender, RoutedEventArgs eventArgs) => OpenProperties();

    private void OnEditorPropertiesRequested(object? sender, EventArgs eventArgs) => OpenProperties();

    private void OpenProperties()
    {
        var editor = EditorCanvas.Editor;
        if (editor is not { CanEditScene: true })
        {
            return;
        }
        if (editor?.SelectedElement?.Definition is CameraElementDefinition)
        {
            if (editor.BeginProperties() is not null)
            {
                ShowPropertiesWindow(new CameraElementPropertiesWindow(editor));
            }
            return;
        }
        if (editor?.BeginVisualProperties() is null)
        {
            return;
        }
        ShowPropertiesWindow(new VisualElementPropertiesWindow(editor));
    }

    private void OnLocateSourceRequested(object? sender, string cameraId)
        => _workspace?.LocateCamera(cameraId);

    private void ShowPropertiesWindow(Window window)
    {
        _propertiesWindow?.Close();
        _propertiesWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_propertiesWindow, window))
            {
                _propertiesWindow = null;
            }
        };
        _ = window.ShowDialog(this);
    }

    private void OnEnterFullscreen(object? sender, RoutedEventArgs eventArgs) => EnterFullscreen();

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.F11 && _fullscreenWindow is null)
        {
            eventArgs.Handled = true;
            EnterFullscreen();
        }
        var commandModifier = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control)
            || eventArgs.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (commandModifier && eventArgs.Key == Key.S)
        {
            eventArgs.Handled = true;
            _ = SaveCurrentAsync(eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
        else if (commandModifier && eventArgs.Key == Key.N)
        {
            eventArgs.Handled = true;
            _ = NewShowAsync();
        }
        else if (commandModifier && eventArgs.Key == Key.O)
        {
            eventArgs.Handled = true;
            _ = PickAndOpenShowAsync();
        }
    }

    private void EnterFullscreen()
    {
        var workspace = _workspace;
        if (workspace is null || _fullscreenWindow is not null || !workspace.EnterFullscreen())
        {
            return;
        }

        ViewPreviewHost.DetachPreview();
        var fullscreen = new FullscreenMonitorWindow(workspace);
        _fullscreenWindow = fullscreen;
        fullscreen.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_fullscreenWindow, fullscreen))
            {
                return;
            }
            _fullscreenWindow = null;
            workspace.ExitFullscreen();
            if (!_shutdownStarted)
            {
                ViewPreviewHost.ReattachPreview();
            }
        };
        fullscreen.Show();
        fullscreen.WindowState = WindowState.FullScreen;
    }

    private void UpdateModePresentation()
    {
        if (_workspace is null)
        {
            return;
        }

        UpdateWindowTitle();
        if (_workspace.IsShowMode)
        {
            _propertiesWindow?.Close();
            EditorPreviewGrid.ColumnDefinitions[0].Width = new GridLength(0);
            EditorPreviewGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(NativePreviewPanel, 0);
            Grid.SetColumnSpan(NativePreviewPanel, 2);
        }
        else
        {
            EditorPreviewGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            EditorPreviewGrid.ColumnDefinitions[1].Width = new GridLength(300);
            Grid.SetColumn(NativePreviewPanel, 1);
            Grid.SetColumnSpan(NativePreviewPanel, 1);
        }
    }

    private async void OnNewShow(object? sender, RoutedEventArgs eventArgs) => await NewShowAsync();

    private async Task NewShowAsync()
    {
        if (_fileOperationInProgress || !await ConfirmSafeToReplaceAsync())
        {
            return;
        }
        await RunFileOperationAsync(async () =>
        {
            var prepared = await _workspaceLoader.NewAsync(_lifetime.Token);
            await ActivatePreparedWorkspaceAsync(prepared);
        });
    }

    private async void OnOpenShow(object? sender, RoutedEventArgs eventArgs) => await PickAndOpenShowAsync();

    private async Task PickAndOpenShowAsync()
    {
        if (_fileOperationInProgress || !await ConfirmSafeToReplaceAsync())
        {
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open RoboCam-Hub Show",
            AllowMultiple = false,
            FileTypeFilter = [ShowFilePickerType()],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        await OpenPathAsync(path);
    }

    private async void OnSaveShow(object? sender, RoutedEventArgs eventArgs) => await SaveCurrentAsync(forceSaveAs: false);

    private async void OnSaveShowAs(object? sender, RoutedEventArgs eventArgs) => await SaveCurrentAsync(forceSaveAs: true);

    private async Task OpenPathAsync(string path)
    {
        await RunFileOperationAsync(async () =>
        {
            var prepared = await _workspaceLoader.OpenAsync(path, _lifetime.Token);
            await ActivatePreparedWorkspaceAsync(prepared);
            await AddRecentFileAsync(path);
            if (prepared.Warnings.Count > 0)
            {
                await prepared.Workspace.ReportPersistenceWarningAsync(string.Join(" ", prepared.Warnings.Select(warning => warning.Message)));
            }
        });
    }

    private async Task<bool> SaveCurrentAsync(bool forceSaveAs)
    {
        if (_workspace is null || _persistence is null || _fileOperationInProgress)
        {
            return false;
        }
        var path = forceSaveAs ? null : _workspace.CurrentFilePath;
        if (path is null)
        {
            var suggestedName = SanitizeFileName(_workspace.ShowName) + ShowFileService.DefaultExtension;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save RoboCam-Hub Show",
                SuggestedFileName = suggestedName,
                DefaultExtension = ShowFileService.DefaultExtension.TrimStart('.'),
                FileTypeChoices = [ShowFilePickerType()],
            });
            path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
        }

        var succeeded = false;
        await RunFileOperationAsync(async () =>
        {
            await _persistence.SaveAsync(path, _lifetime.Token);
            await AddRecentFileAsync(ShowFileService.EnsureExtension(path));
            succeeded = true;
        });
        return succeeded;
    }

    private async Task<bool> ConfirmSafeToReplaceAsync()
    {
        if (_workspace is not { IsDirty: true })
        {
            return true;
        }
        var decision = await new SaveChangesWindow(_workspace.ShowFileDisplayName)
            .ShowDialog<SaveChangesDecision>(this);
        return decision switch
        {
            SaveChangesDecision.DontSave => true,
            SaveChangesDecision.Save => await SaveCurrentAsync(forceSaveAs: false),
            _ => false,
        };
    }

    private async Task ActivatePreparedWorkspaceAsync(PreparedWorkspace prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        _propertiesWindow?.Close();
        _propertiesWindow = null;
        _fullscreenWindow?.Close();
        _fullscreenWindow = null;
        ViewPreviewHost.DetachPreview();
        EditorCanvas.Editor = null;
        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        }
        if (_persistence is not null)
        {
            await _persistence.DisposeAsync();
        }
        if (_preparedWorkspace is not null)
        {
            await _preparedWorkspace.DisposeAsync();
        }

        _preparedWorkspace = prepared;
        _workspace = prepared.Workspace;
        await _workspace.StartConfiguredRuntimeAsync(_lifetime.Token);
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        _persistence = new WorkspacePersistenceCoordinator(_workspace, _showFiles, _recovery);
        DataContext = _workspace;
        ViewPreviewHost.Preview = _workspace.Preview;
        EditorCanvas.Editor = _workspace.SelectedView.Editor;
        _workspace.StartStatusPolling();
        UpdateModePresentation();
    }

    private async Task RunFileOperationAsync(Func<Task> operation)
    {
        _fileOperationInProgress = true;
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_workspace is not null)
            {
                await _workspace.ReportPersistenceErrorAsync("operation", exception);
            }
            else
            {
                StartupText.Text = $"Show file operation failed: {exception.Message}";
            }
        }
        finally
        {
            _fileOperationInProgress = false;
        }
    }

    private async Task AddRecentFileAsync(string path)
    {
        path = Path.GetFullPath(path);
        _preferences.RecentFiles.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
        _preferences.RecentFiles.Insert(0, path);
        if (_preferences.RecentFiles.Count > 10)
        {
            _preferences.RecentFiles.RemoveRange(10, _preferences.RecentFiles.Count - 10);
        }
        _preferences.LastFolder = Path.GetDirectoryName(path);
        UpdateRecentMenu();
        await _preferencesStore.SaveAsync(_preferences, _lifetime.Token);
    }

    private void UpdateRecentMenu()
    {
        var items = _preferences.RecentFiles.Select(path =>
        {
            var item = new MenuItem { Header = path };
            item.Click += async (_, _) =>
            {
                if (!_fileOperationInProgress && await ConfirmSafeToReplaceAsync())
                {
                    await OpenPathAsync(path);
                }
            };
            return item;
        }).ToArray();
        RecentMenu.ItemsSource = items;
        RecentMenu.IsEnabled = items.Length > 0;
    }

    private void ApplyMachinePreferences()
    {
        if (_preferences.WindowWidth is >= 1120 and <= 10000)
        {
            Width = _preferences.WindowWidth.Value;
        }
        if (_preferences.WindowHeight is >= 700 and <= 10000)
        {
            Height = _preferences.WindowHeight.Value;
        }
        if (_preferences.WindowX is >= -50000 and <= 50000 && _preferences.WindowY is >= -50000 and <= 50000)
        {
            Position = new PixelPoint((int)_preferences.WindowX.Value, (int)_preferences.WindowY.Value);
        }
        if (Enum.TryParse<WindowState>(_preferences.WindowState, out var state) && state != WindowState.Minimized)
        {
            WindowState = state;
        }
        UpdateRecentMenu();
    }

    private async Task SaveMachinePreferencesAsync()
    {
        _preferences.WindowX = Position.X;
        _preferences.WindowY = Position.Y;
        _preferences.WindowWidth = Width;
        _preferences.WindowHeight = Height;
        _preferences.WindowState = WindowState == WindowState.Minimized ? nameof(WindowState.Normal) : WindowState.ToString();
        try
        {
            await _preferencesStore.SaveAsync(_preferences);
        }
        catch
        {
            // Machine preferences are never allowed to block show/runtime shutdown.
        }
    }

    private void UpdateWindowTitle()
    {
        if (_workspace is not null)
        {
            Title = _workspace.WindowTitle;
        }
    }

    private static FilePickerFileType ShowFilePickerType()
        => new("RoboCam-Hub Show")
        {
            Patterns = ["*.rchshow"],
            MimeTypes = ["application/vnd.robocamhub.show+zip"],
        };

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrEmpty(sanitized) ? "Untitled Show" : sanitized;
    }
}
