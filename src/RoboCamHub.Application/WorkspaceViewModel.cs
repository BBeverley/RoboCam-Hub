using System.Collections.ObjectModel;
using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public sealed class WorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private IWorkspaceRuntimeService? _runtime;
    private readonly IUiDispatcher _dispatcher;
    private readonly StatusPollingService _polling;
    private bool _isAddingCamera;
    private bool _isAddingOutput;
    private string _newCameraName = string.Empty;
    private string _newCameraRtspUrl = string.Empty;
    private string _newOutputName = "Spots A";
    private string _newOutputNdiSourceName = "ROBOCAM - SPOTS A";
    private string? _workspaceMessage;
    private int _disposed;

    public WorkspaceViewModel(
        IWorkspaceRuntimeService runtime,
        IUiDispatcher? dispatcher = null,
        TimeSpan? pollingInterval = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatcher = dispatcher ?? new ImmediateUiDispatcher();
        Cameras = new ObservableCollection<CameraItemViewModel>(
            runtime.CameraDefinitions.Select(
                definition => new CameraItemViewModel(definition, runtime, _dispatcher)));
        View = new ViewWorkspaceViewModel(runtime.ViewDefinition, Cameras, runtime, _dispatcher);
        Preview = new ViewPreviewViewModel(runtime);
        Outputs = [];
        if (runtime.OutputDefinition is { } outputDefinition)
        {
            Outputs.Add(new OutputItemViewModel(outputDefinition, View.Name, runtime, _dispatcher));
        }

        AddCameraCommand = new AsyncCommand(AddCameraAsync, () => CanAddCamera);
        AddOutputCommand = new AsyncCommand(AddOutputAsync, () => CanAddOutput);
        _polling = new StatusPollingService(
            RefreshStatusAsync,
            pollingInterval ?? TimeSpan.FromMilliseconds(333),
            OnPollingErrorAsync);
    }

    public string ApplicationTitle => "RoboCam-Hub";

    public string ModeText => "EDIT MODE";

    public ObservableCollection<CameraItemViewModel> Cameras { get; }

    public ViewWorkspaceViewModel View { get; }

    public ViewPreviewViewModel Preview { get; }

    public string OutputViewLabel => $"View: {View.Name}";

    public ObservableCollection<OutputItemViewModel> Outputs { get; }

    public string NewCameraName
    {
        get => _newCameraName;
        set
        {
            if (SetProperty(ref _newCameraName, value))
            {
                RaiseAddCameraCommandState();
            }
        }
    }

    public string NewCameraRtspUrl
    {
        get => _newCameraRtspUrl;
        set
        {
            if (SetProperty(ref _newCameraRtspUrl, value))
            {
                RaiseAddCameraCommandState();
            }
        }
    }

    public string NewOutputName
    {
        get => _newOutputName;
        set
        {
            if (SetProperty(ref _newOutputName, value))
            {
                RaiseAddOutputCommandState();
            }
        }
    }

    public string NewOutputNdiSourceName
    {
        get => _newOutputNdiSourceName;
        set
        {
            if (SetProperty(ref _newOutputNdiSourceName, value))
            {
                RaiseAddOutputCommandState();
            }
        }
    }

    public bool IsAddingCamera
    {
        get => _isAddingCamera;
        private set
        {
            if (SetProperty(ref _isAddingCamera, value))
            {
                RaiseAddCameraCommandState();
            }
        }
    }

    public bool IsAddingOutput
    {
        get => _isAddingOutput;
        private set
        {
            if (SetProperty(ref _isAddingOutput, value))
            {
                RaiseAddOutputCommandState();
            }
        }
    }

    public bool CanAddCamera => _runtime is not null
        && !IsAddingCamera
        && !string.IsNullOrWhiteSpace(NewCameraName)
        && !string.IsNullOrWhiteSpace(NewCameraRtspUrl);

    public bool CanAddOutput => _runtime is not null
        && Outputs.Count == 0
        && !IsAddingOutput
        && !string.IsNullOrWhiteSpace(NewOutputName)
        && !string.IsNullOrWhiteSpace(NewOutputNdiSourceName);

    public bool ShowOutputConfiguration => Outputs.Count == 0;

    public string? WorkspaceMessage
    {
        get => _workspaceMessage;
        private set
        {
            if (SetProperty(ref _workspaceMessage, value))
            {
                RaisePropertyChanged(nameof(HasWorkspaceMessage));
            }
        }
    }

    public bool HasWorkspaceMessage => !string.IsNullOrWhiteSpace(WorkspaceMessage);

    public AsyncCommand AddCameraCommand { get; }

    public AsyncCommand AddOutputCommand { get; }

    public void StartStatusPolling() => _polling.Start();

    internal Task RefreshNowAsync(CancellationToken cancellationToken = default)
        => RefreshStatusAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _polling.DisposeAsync().ConfigureAwait(false);
        foreach (var output in Outputs)
        {
            output.Dispose();
        }

        Preview.Dispose();
        View.Dispose();
        foreach (var camera in Cameras)
        {
            camera.Dispose();
        }

        var runtime = Interlocked.Exchange(ref _runtime, null);
        RaiseAddCameraCommandState();
        RaiseAddOutputCommandState();
        if (runtime is not null)
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task AddCameraAsync()
    {
        var runtime = _runtime;
        if (runtime is null || IsAddingCamera)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsAddingCamera = true;
            WorkspaceMessage = null;
        }).ConfigureAwait(false);
        try
        {
            var definition = new CameraDefinition(
                $"camera-{Guid.NewGuid():N}",
                NewCameraName.Trim(),
                NewCameraRtspUrl.Trim());
            await runtime.AddCameraAsync(definition).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                Cameras.Add(new CameraItemViewModel(definition, runtime, _dispatcher));
                NewCameraName = string.Empty;
                NewCameraRtspUrl = string.Empty;
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(
                () => WorkspaceMessage = OperatorError.ForAction("Camera", "creation", exception))
                .ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsAddingCamera = false).ConfigureAwait(false);
        }
    }

    private async Task AddOutputAsync()
    {
        var runtime = _runtime;
        if (runtime is null || IsAddingOutput || Outputs.Count != 0)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsAddingOutput = true;
            WorkspaceMessage = null;
        }).ConfigureAwait(false);
        try
        {
            var definition = new OutputDefinition(
                $"output-{Guid.NewGuid():N}",
                NewOutputName.Trim(),
                NewOutputNdiSourceName.Trim(),
                View.Definition.Id);
            await runtime.AddOutputAsync(definition).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                Outputs.Add(new OutputItemViewModel(definition, View.Name, runtime, _dispatcher));
                RaisePropertyChanged(nameof(ShowOutputConfiguration));
                RaiseAddOutputCommandState();
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(
                () => WorkspaceMessage = OperatorError.ForAction("Output", "creation", exception))
                .ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsAddingOutput = false).ConfigureAwait(false);
        }
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        var runtime = _runtime;
        if (runtime is null)
        {
            return;
        }

        var snapshot = await runtime.QueryStatusAsync(cancellationToken).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() => ApplySnapshot(snapshot)).ConfigureAwait(false);
    }

    private void ApplySnapshot(WorkspaceRuntimeSnapshot snapshot)
    {
        foreach (var camera in Cameras)
        {
            if (snapshot.Cameras.TryGetValue(camera.Definition.Id, out var observation))
            {
                camera.ApplyStatus(observation);
            }
        }

        View.ApplySnapshot(snapshot);
        Preview.ApplyStatus(snapshot.Preview);
        foreach (var output in Outputs)
        {
            if (snapshot.Outputs.TryGetValue(output.Definition.Id, out var observation))
            {
                output.ApplyStatus(observation);
            }
        }
    }

    private Task OnPollingErrorAsync(Exception exception)
        => _dispatcher.InvokeAsync(
            () => WorkspaceMessage = OperatorError.ForAction("Runtime status", "refresh", exception));

    private void RaiseAddCameraCommandState()
    {
        RaisePropertyChanged(nameof(CanAddCamera));
        AddCameraCommand.RaiseCanExecuteChanged();
    }

    private void RaiseAddOutputCommandState()
    {
        RaisePropertyChanged(nameof(CanAddOutput));
        AddOutputCommand.RaiseCanExecuteChanged();
    }
}
