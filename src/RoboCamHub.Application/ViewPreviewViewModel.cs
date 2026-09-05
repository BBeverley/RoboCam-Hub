using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public sealed class ViewPreviewViewModel : ObservableObject, IDisposable
{
    private IWorkspaceRuntimeService? _runtime;
    private string _selectedViewId;
    private ViewPreviewRuntimeState _state = ViewPreviewRuntimeState.Starting;
    private bool _attached;
    private uint _presentationFpsMilli;
    private ulong _latestPresentedSequence;
    private ulong _latestPresentedFrameAgeMs = ulong.MaxValue;
    private ulong _droppedOrSkippedFrameCount;
    private uint _surfaceRecreateCount;
    private string? _operatorMessage;

    internal ViewPreviewViewModel(IWorkspaceRuntimeService runtime, string selectedViewId)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _selectedViewId = selectedViewId;
    }

    public string SelectedViewId
    {
        get => _selectedViewId;
        private set => SetProperty(ref _selectedViewId, value);
    }

    public ViewPreviewRuntimeState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                RaisePropertyChanged(nameof(StateText));
                RaisePropertyChanged(nameof(HealthIcon));
                RaisePropertyChanged(nameof(HealthColor));
            }
        }
    }

    public string StateText => State switch
    {
        ViewPreviewRuntimeState.Live => "Preview Live",
        ViewPreviewRuntimeState.WaitingForView => "Preview Waiting for View",
        ViewPreviewRuntimeState.Failed => "Preview Failed",
        _ => "Preview Starting",
    };

    public string HealthIcon => State switch
    {
        ViewPreviewRuntimeState.Live => "●",
        ViewPreviewRuntimeState.Failed => "✕",
        _ => "◐",
    };

    public string HealthColor => State switch
    {
        ViewPreviewRuntimeState.Live => "#45C782",
        ViewPreviewRuntimeState.Failed => "#F06A6A",
        _ => "#F5B84B",
    };

    public bool Attached
    {
        get => _attached;
        private set => SetProperty(ref _attached, value);
    }

    public uint PresentationFpsMilli
    {
        get => _presentationFpsMilli;
        private set
        {
            if (SetProperty(ref _presentationFpsMilli, value))
            {
                RaisePropertyChanged(nameof(PresentationFpsText));
            }
        }
    }

    public string PresentationFpsText => $"Preview {PresentationFpsMilli / 1000.0:F1} fps";

    public ulong LatestPresentedSequence
    {
        get => _latestPresentedSequence;
        private set => SetProperty(ref _latestPresentedSequence, value);
    }

    public ulong LatestPresentedFrameAgeMs
    {
        get => _latestPresentedFrameAgeMs;
        private set
        {
            if (SetProperty(ref _latestPresentedFrameAgeMs, value))
            {
                RaisePropertyChanged(nameof(FrameAgeText));
            }
        }
    }

    public string FrameAgeText => LatestPresentedFrameAgeMs == ulong.MaxValue
        ? "Frame age —"
        : $"Frame age {LatestPresentedFrameAgeMs} ms";

    public ulong DroppedOrSkippedFrameCount
    {
        get => _droppedOrSkippedFrameCount;
        private set => SetProperty(ref _droppedOrSkippedFrameCount, value);
    }

    public uint SurfaceRecreateCount
    {
        get => _surfaceRecreateCount;
        private set => SetProperty(ref _surfaceRecreateCount, value);
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

    public void Attach(PreviewHostSurface host)
    {
        var runtime = _runtime;
        if (runtime is null)
        {
            return;
        }
        State = ViewPreviewRuntimeState.Starting;
        OperatorMessage = null;
        try
        {
            runtime.AttachPreview(SelectedViewId, host);
            Attached = true;
        }
        catch (Exception exception)
        {
            Attached = false;
            State = ViewPreviewRuntimeState.Failed;
            OperatorMessage = OperatorError.ForAction("Preview", "attach", exception);
        }
    }

    internal bool TrySwitchView(string viewId)
    {
        var runtime = _runtime;
        if (runtime is null || string.Equals(SelectedViewId, viewId, StringComparison.Ordinal))
        {
            return runtime is not null;
        }

        State = ViewPreviewRuntimeState.Starting;
        OperatorMessage = null;
        try
        {
            runtime.SwitchPreviewView(viewId);
            SelectedViewId = viewId;
            return true;
        }
        catch (Exception exception)
        {
            State = ViewPreviewRuntimeState.Failed;
            OperatorMessage = OperatorError.ForAction("Preview", "switch", exception);
            return false;
        }
    }

    public void Detach()
    {
        var runtime = _runtime;
        if (runtime is null || !Attached)
        {
            return;
        }
        try
        {
            runtime.DetachPreview();
        }
        catch (Exception exception)
        {
            State = ViewPreviewRuntimeState.Failed;
            OperatorMessage = OperatorError.ForAction("Preview", "detach", exception);
        }
        finally
        {
            Attached = false;
        }
    }

    internal void ApplyStatus(RuntimeObservation<ViewPreviewRuntimeStatus>? observation)
    {
        if (observation is null)
        {
            return;
        }
        if (!observation.Value.IsSuccess)
        {
            OperatorMessage = observation.Value.ErrorMessage;
            return;
        }
        var status = observation.Value.Value!.Value;
        State = status.State;
        Attached = status.Attached;
        PresentationFpsMilli = status.PresentationFpsMilli;
        LatestPresentedSequence = status.LatestPresentedSequence;
        LatestPresentedFrameAgeMs = status.LatestPresentedFrameAgeMs;
        DroppedOrSkippedFrameCount = status.DroppedOrSkippedFrameCount;
        SurfaceRecreateCount = status.SurfaceRecreateCount;
        OperatorMessage = status.State == ViewPreviewRuntimeState.Failed
            ? $"Preview failed ({status.LastResult})."
            : null;
    }

    public void Dispose()
    {
        Detach();
        _runtime = null;
    }
}
