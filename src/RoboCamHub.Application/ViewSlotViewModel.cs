using System.Collections.ObjectModel;
using System.ComponentModel;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public sealed class ViewSlotViewModel : ObservableObject, IDisposable
{
    private IWorkspaceRuntimeService? _runtime;
    private readonly IUiDispatcher _dispatcher;
    private readonly string _viewId;
    private readonly WorkspaceCapabilities _capabilities;
    private string? _assignedCameraId;
    private string _assignedCameraName = "Unassigned";
    private CameraItemViewModel? _selectedCamera;
    private ViewSourceRuntimeState _sourceState;
    private bool _isBusy;
    private string? _operatorMessage;

    internal ViewSlotViewModel(
        string viewId,
        uint slotIndex,
        string? initialCameraId,
        ObservableCollection<CameraItemViewModel> cameras,
        IWorkspaceRuntimeService runtime,
        IUiDispatcher dispatcher,
        WorkspaceCapabilities capabilities)
    {
        _viewId = viewId;
        SlotIndex = slotIndex;
        AvailableCameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _capabilities.PropertyChanged += OnCapabilitiesChanged;
        _assignedCameraId = initialCameraId;
        _assignedCameraName = ResolveCameraName(initialCameraId);
        _selectedCamera = cameras.FirstOrDefault(camera => camera.Definition.Id == initialCameraId);
        _sourceState = initialCameraId is null
            ? ViewSourceRuntimeState.Unbound
            : ViewSourceRuntimeState.WaitingForFirstFrame;
        AssignCommand = new AsyncCommand(AssignAsync, () => CanAssign);
        RemoveCommand = new AsyncCommand(RemoveAsync, () => CanRemove);
    }

    public uint SlotIndex { get; }

    public uint SlotNumber => SlotIndex + 1;

    public string SlotLabel => $"Slot {SlotNumber}";

    public ObservableCollection<CameraItemViewModel> AvailableCameras { get; }

    public string? AssignedCameraId
    {
        get => _assignedCameraId;
        private set => SetProperty(ref _assignedCameraId, value);
    }

    public string AssignedCameraName
    {
        get => _assignedCameraName;
        private set => SetProperty(ref _assignedCameraName, value);
    }

    public CameraItemViewModel? SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (SetProperty(ref _selectedCamera, value))
            {
                RaiseCommandState();
            }
        }
    }

    public ViewSourceRuntimeState SourceState
    {
        get => _sourceState;
        private set
        {
            if (SetProperty(ref _sourceState, value))
            {
                RaisePropertyChanged(nameof(SourceStateText));
                RaisePropertyChanged(nameof(HealthIcon));
                RaisePropertyChanged(nameof(HealthColor));
            }
        }
    }

    public string SourceStateText => SourceState switch
    {
        ViewSourceRuntimeState.WaitingForFirstFrame => "Waiting for First Frame",
        ViewSourceRuntimeState.Live => "Live",
        ViewSourceRuntimeState.FrozenLastGood => "Frozen — Last Good Frame",
        ViewSourceRuntimeState.Reconnecting => "Reconnecting",
        ViewSourceRuntimeState.MissingOrStale => "Missing or Stale",
        _ => "Unbound",
    };

    public string HealthIcon => SourceState switch
    {
        ViewSourceRuntimeState.Live => "●",
        ViewSourceRuntimeState.WaitingForFirstFrame or ViewSourceRuntimeState.Reconnecting => "◐",
        ViewSourceRuntimeState.FrozenLastGood => "❄",
        ViewSourceRuntimeState.MissingOrStale => "✕",
        _ => "○",
    };

    public string HealthColor => SourceState switch
    {
        ViewSourceRuntimeState.Live => "#45C782",
        ViewSourceRuntimeState.WaitingForFirstFrame or ViewSourceRuntimeState.Reconnecting => "#F5B84B",
        ViewSourceRuntimeState.FrozenLastGood => "#67B7FF",
        ViewSourceRuntimeState.MissingOrStale => "#F06A6A",
        _ => "#8E99A8",
    };

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandState();
            }
        }
    }

    public string? OperatorMessage
    {
        get => _operatorMessage;
        private set
        {
            if (SetProperty(ref _operatorMessage, value))
            {
                RaisePropertyChanged(nameof(HasOperatorMessage));
            }
        }
    }

    public bool HasOperatorMessage => !string.IsNullOrWhiteSpace(OperatorMessage);

    public bool CanAssign => _runtime is not null
        && _capabilities.CanEditCameraAssignments
        && !IsBusy
        && SelectedCamera is not null
        && !string.Equals(SelectedCamera.Definition.Id, AssignedCameraId, StringComparison.Ordinal);

    public bool CanRemove => _runtime is not null
        && _capabilities.CanEditCameraAssignments
        && !IsBusy
        && AssignedCameraId is not null;

    public AsyncCommand AssignCommand { get; }

    public AsyncCommand RemoveCommand { get; }

    internal void ApplyStatus(RuntimeObservation<ViewSourceRuntimeStatus> observation)
    {
        if (!observation.IsSuccess)
        {
            OperatorMessage = observation.ErrorMessage;
            return;
        }

        var status = observation.Value!.Value;
        ApplyLiveAssignment(status.HasBinding ? status.CameraId : null);
        SourceState = status.State;
        OperatorMessage = null;
    }

    public void Dispose()
    {
        _capabilities.PropertyChanged -= OnCapabilitiesChanged;
        _runtime = null;
        RaiseCommandState();
    }

    private async Task AssignAsync()
    {
        var runtime = _runtime;
        var selected = SelectedCamera;
        if (runtime is null || selected is null || IsBusy)
        {
            return;
        }

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            await runtime.BindCameraSourceAsync(
                _viewId,
                SlotIndex,
                selected.Definition.Id).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                ApplyLiveAssignment(selected.Definition.Id);
                SourceState = ViewSourceRuntimeState.WaitingForFirstFrame;
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(
                () => OperatorMessage = OperatorError.ForAction($"Slot {SlotNumber}", "assignment", exception))
                .ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    private async Task RemoveAsync()
    {
        var runtime = _runtime;
        if (runtime is null || IsBusy || AssignedCameraId is null)
        {
            return;
        }

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            await runtime.UnbindSourceAsync(_viewId, SlotIndex).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                ApplyLiveAssignment(null);
                SourceState = ViewSourceRuntimeState.Unbound;
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(
                () => OperatorMessage = OperatorError.ForAction($"Slot {SlotNumber}", "removal", exception))
                .ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    private Task SetBusyAsync(bool value)
        => _dispatcher.InvokeAsync(() =>
        {
            IsBusy = value;
            if (value)
            {
                OperatorMessage = null;
            }
        });

    private void ApplyLiveAssignment(string? cameraId)
    {
        var assignmentChanged = !string.Equals(AssignedCameraId, cameraId, StringComparison.Ordinal);
        AssignedCameraId = cameraId;
        AssignedCameraName = ResolveCameraName(cameraId);
        if (assignmentChanged)
        {
            SelectedCamera = AvailableCameras.FirstOrDefault(camera => camera.Definition.Id == cameraId);
        }
        RaiseCommandState();
    }

    private string ResolveCameraName(string? cameraId)
        => cameraId is null
            ? "Unassigned"
            : AvailableCameras.FirstOrDefault(camera => camera.Definition.Id == cameraId)?.Name ?? cameraId;

    private void RaiseCommandState()
    {
        RaisePropertyChanged(nameof(CanAssign));
        RaisePropertyChanged(nameof(CanRemove));
        AssignCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
    }

    private void OnCapabilitiesChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(WorkspaceCapabilities.Mode)
            or nameof(WorkspaceCapabilities.CanEditCameraAssignments))
        {
            RaiseCommandState();
        }
    }
}
