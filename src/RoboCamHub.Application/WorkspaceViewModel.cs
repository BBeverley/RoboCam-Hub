using System.Collections.ObjectModel;
using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public sealed class WorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private IWorkspaceRuntimeService? _runtime;
    private readonly IUiDispatcher _dispatcher;
    private readonly StatusPollingService _polling;
    private bool _isAddingCamera;
    private bool _isAddingView;
    private bool _isSelectingView;
    private bool _isAddingOutput;
    private string _newCameraName = string.Empty;
    private string _newCameraRtspUrl = string.Empty;
    private string _newViewName = "Spots B";
    private string _newOutputName = "Spots A";
    private string _newOutputNdiSourceName = "ROBOCAM - SPOTS A";
    private ViewWorkspaceViewModel _selectedView;
    private ViewWorkspaceViewModel? _pendingSelectedView;
    private ViewWorkspaceViewModel? _pendingOutputView;
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
        Views = new ObservableCollection<ViewWorkspaceViewModel>(
            runtime.ViewDefinitions.Select(
                definition => new ViewWorkspaceViewModel(definition, Cameras, runtime, _dispatcher)));
        _selectedView = Views.FirstOrDefault(view => string.Equals(
                view.Definition.Id,
                runtime.SelectedViewId,
                StringComparison.Ordinal))
            ?? Views.FirstOrDefault()
            ?? throw new InvalidOperationException("The workspace requires at least one View.");
        _pendingSelectedView = _selectedView;
        _pendingOutputView = _selectedView;
        Preview = new ViewPreviewViewModel(runtime, _selectedView.Definition.Id);
        Outputs = new ObservableCollection<OutputItemViewModel>(
            runtime.OutputDefinitions.Select(definition => CreateOutputViewModel(definition, runtime)));

        AddCameraCommand = new AsyncCommand(AddCameraAsync, () => CanAddCamera);
        AddViewCommand = new AsyncCommand(AddViewAsync, () => CanAddView);
        SelectViewCommand = new AsyncCommand(SelectViewAsync, () => CanSelectView);
        AddOutputCommand = new AsyncCommand(AddOutputAsync, () => CanAddOutput);
        _polling = new StatusPollingService(
            RefreshStatusAsync,
            pollingInterval ?? TimeSpan.FromMilliseconds(333),
            OnPollingErrorAsync);
    }

    public string ApplicationTitle => "RoboCam-Hub";

    public string ModeText => "EDIT MODE";

    public ObservableCollection<CameraItemViewModel> Cameras { get; }

    public uint ActiveRtspSessionTotal
        => (uint)Cameras.Sum(camera => camera.ActiveRtspSessionCount);

    public uint ActiveDecoderTotal
        => (uint)Cameras.Sum(camera => camera.ActiveDecoderCount);

    public string MediaOwnershipText => $"RTSP / decoders {ActiveRtspSessionTotal} / {ActiveDecoderTotal}";

    public ObservableCollection<ViewWorkspaceViewModel> Views { get; }

    public ViewWorkspaceViewModel SelectedView
    {
        get => _selectedView;
        private set => SetProperty(ref _selectedView, value);
    }

    public ViewWorkspaceViewModel? PendingSelectedView
    {
        get => _pendingSelectedView;
        set
        {
            if (SetProperty(ref _pendingSelectedView, value))
            {
                RaiseSelectViewCommandState();
            }
        }
    }

    public ViewWorkspaceViewModel? PendingOutputView
    {
        get => _pendingOutputView;
        set
        {
            if (SetProperty(ref _pendingOutputView, value))
            {
                RaiseAddOutputCommandState();
            }
        }
    }

    public ViewPreviewViewModel Preview { get; }

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

    public string NewViewName
    {
        get => _newViewName;
        set
        {
            if (SetProperty(ref _newViewName, value))
            {
                RaiseAddViewCommandState();
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

    public bool IsAddingView
    {
        get => _isAddingView;
        private set
        {
            if (SetProperty(ref _isAddingView, value))
            {
                RaiseAddViewCommandState();
            }
        }
    }

    public bool IsSelectingView
    {
        get => _isSelectingView;
        private set
        {
            if (SetProperty(ref _isSelectingView, value))
            {
                RaiseSelectViewCommandState();
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

    public bool CanAddView => _runtime is not null
        && !IsAddingView
        && !string.IsNullOrWhiteSpace(NewViewName);

    public bool CanSelectView => _runtime is not null
        && !IsSelectingView
        && PendingSelectedView is not null
        && !ReferenceEquals(PendingSelectedView, SelectedView);

    public bool CanAddOutput => _runtime is not null
        && !IsAddingOutput
        && PendingOutputView is not null
        && !string.IsNullOrWhiteSpace(NewOutputName)
        && !string.IsNullOrWhiteSpace(NewOutputNdiSourceName);

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

    public AsyncCommand AddViewCommand { get; }

    public AsyncCommand SelectViewCommand { get; }

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
        foreach (var view in Views)
        {
            view.Dispose();
        }
        foreach (var camera in Cameras)
        {
            camera.Dispose();
        }

        var runtime = Interlocked.Exchange(ref _runtime, null);
        RaiseAllCommandStates();
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

    private async Task AddViewAsync()
    {
        var runtime = _runtime;
        if (runtime is null || IsAddingView)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsAddingView = true;
            WorkspaceMessage = null;
        }).ConfigureAwait(false);
        try
        {
            var definition = new ViewDefinition(
                $"view-{Guid.NewGuid():N}",
                NewViewName.Trim());
            await runtime.AddViewAsync(definition).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                var view = new ViewWorkspaceViewModel(definition, Cameras, runtime, _dispatcher);
                Views.Add(view);
                PendingSelectedView = view;
                PendingOutputView ??= view;
                NewViewName = string.Empty;
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(
                () => WorkspaceMessage = OperatorError.ForAction("View", "creation", exception))
                .ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsAddingView = false).ConfigureAwait(false);
        }
    }

    private Task SelectViewAsync()
        => _dispatcher.InvokeAsync(() =>
        {
            var target = PendingSelectedView;
            if (target is null || ReferenceEquals(target, SelectedView))
            {
                return;
            }

            IsSelectingView = true;
            WorkspaceMessage = null;
            try
            {
                if (Preview.TrySwitchView(target.Definition.Id))
                {
                    SelectedView = target;
                }
            }
            finally
            {
                IsSelectingView = false;
            }
        });

    private async Task AddOutputAsync()
    {
        var runtime = _runtime;
        var targetView = PendingOutputView;
        if (runtime is null || targetView is null || IsAddingOutput)
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
                targetView.Definition.Id);
            await runtime.AddOutputAsync(definition).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                Outputs.Add(new OutputItemViewModel(definition, targetView.Name, runtime, _dispatcher));
                NewOutputName = string.Empty;
                NewOutputNdiSourceName = "ROBOCAM - ";
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
        RaisePropertyChanged(nameof(ActiveRtspSessionTotal));
        RaisePropertyChanged(nameof(ActiveDecoderTotal));
        RaisePropertyChanged(nameof(MediaOwnershipText));

        foreach (var view in Views)
        {
            view.ApplySnapshot(snapshot);
        }
        Preview.ApplyStatus(snapshot.Preview);
        foreach (var output in Outputs)
        {
            if (snapshot.Outputs.TryGetValue(output.Definition.Id, out var observation))
            {
                output.ApplyStatus(observation);
            }
        }
    }

    private OutputItemViewModel CreateOutputViewModel(
        OutputDefinition definition,
        IWorkspaceRuntimeService runtime)
    {
        var viewName = Views.FirstOrDefault(view => string.Equals(
                view.Definition.Id,
                definition.ViewId,
                StringComparison.Ordinal))?.Name
            ?? definition.ViewId;
        return new OutputItemViewModel(definition, viewName, runtime, _dispatcher);
    }

    private Task OnPollingErrorAsync(Exception exception)
        => _dispatcher.InvokeAsync(
            () => WorkspaceMessage = OperatorError.ForAction("Runtime status", "refresh", exception));

    private void RaiseAllCommandStates()
    {
        RaiseAddCameraCommandState();
        RaiseAddViewCommandState();
        RaiseSelectViewCommandState();
        RaiseAddOutputCommandState();
    }

    private void RaiseAddCameraCommandState()
    {
        RaisePropertyChanged(nameof(CanAddCamera));
        AddCameraCommand.RaiseCanExecuteChanged();
    }

    private void RaiseAddViewCommandState()
    {
        RaisePropertyChanged(nameof(CanAddView));
        AddViewCommand.RaiseCanExecuteChanged();
    }

    private void RaiseSelectViewCommandState()
    {
        RaisePropertyChanged(nameof(CanSelectView));
        SelectViewCommand.RaiseCanExecuteChanged();
    }

    private void RaiseAddOutputCommandState()
    {
        RaisePropertyChanged(nameof(CanAddOutput));
        AddOutputCommand.RaiseCanExecuteChanged();
    }
}
