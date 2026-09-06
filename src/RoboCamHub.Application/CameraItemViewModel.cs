using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public sealed class CameraItemViewModel : ObservableObject, IDisposable
{
    private IWorkspaceRuntimeService? _runtime;
    private readonly IUiDispatcher _dispatcher;
    private CameraRuntimeState _state = CameraRuntimeState.Stopped;
    private uint _activeRtspSessionCount;
    private uint _activeDecoderCount;
    private uint _latestFrameWidth;
    private uint _latestFrameHeight;
    private bool _isBusy;
    private bool _isLocatedInEditor;
    private string? _operatorMessage;

    public CameraItemViewModel(
        CameraDefinition definition,
        IWorkspaceRuntimeService runtime,
        IUiDispatcher dispatcher)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        StartCommand = new AsyncCommand(StartAsync, () => CanStart);
        StopCommand = new AsyncCommand(StopAsync, () => CanStop);
    }

    public CameraDefinition Definition { get; }

    public string Name => Definition.Name;

    public string AddressSummary => Definition.RtspUrl;

    public CameraRuntimeState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                RaisePropertyChanged(nameof(StateText));
                RaisePropertyChanged(nameof(HealthIcon));
                RaisePropertyChanged(nameof(HealthColor));
                RaiseCommandState();
            }
        }
    }

    public string StateText => State switch
    {
        CameraRuntimeState.Receiving => "Receiving",
        CameraRuntimeState.Starting => "Starting",
        CameraRuntimeState.WaitingToRetry => "Waiting to Retry",
        CameraRuntimeState.Failed => "Failed",
        CameraRuntimeState.Stopping => "Stopping",
        _ => "Stopped",
    };

    public string HealthIcon => State switch
    {
        CameraRuntimeState.Receiving => "●",
        CameraRuntimeState.Starting or CameraRuntimeState.Stopping => "◐",
        CameraRuntimeState.WaitingToRetry => "▲",
        CameraRuntimeState.Failed => "✕",
        _ => "○",
    };

    public string HealthColor => State switch
    {
        CameraRuntimeState.Receiving => "#45C782",
        CameraRuntimeState.Starting or CameraRuntimeState.Stopping => "#67B7FF",
        CameraRuntimeState.WaitingToRetry => "#F5B84B",
        CameraRuntimeState.Failed => "#F06A6A",
        _ => "#8E99A8",
    };

    public uint ActiveRtspSessionCount
    {
        get => _activeRtspSessionCount;
        private set => SetProperty(ref _activeRtspSessionCount, value);
    }

    public uint ActiveDecoderCount
    {
        get => _activeDecoderCount;
        private set => SetProperty(ref _activeDecoderCount, value);
    }

    public uint LatestFrameWidth
    {
        get => _latestFrameWidth;
        private set => SetProperty(ref _latestFrameWidth, value);
    }

    public uint LatestFrameHeight
    {
        get => _latestFrameHeight;
        private set => SetProperty(ref _latestFrameHeight, value);
    }

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

    public bool IsLocatedInEditor
    {
        get => _isLocatedInEditor;
        internal set => SetProperty(ref _isLocatedInEditor, value);
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

    public bool CanStart => _runtime is not null
        && Definition.Enabled
        && !IsBusy
        && State is CameraRuntimeState.Stopped or CameraRuntimeState.Failed;

    public bool CanStop => _runtime is not null
        && !IsBusy
        && State is not CameraRuntimeState.Stopped;

    public AsyncCommand StartCommand { get; }

    public AsyncCommand StopCommand { get; }

    internal void ApplyStatus(RuntimeObservation<CameraRuntimeStatus> observation)
    {
        if (!observation.IsSuccess)
        {
            OperatorMessage = observation.ErrorMessage;
            return;
        }

        State = observation.Value!.Value.State;
        ActiveRtspSessionCount = observation.Value.Value.ActiveRtspSessionCount;
        ActiveDecoderCount = observation.Value.Value.ActiveDecoderCount;
        LatestFrameWidth = observation.Value.Value.LatestFrameWidth;
        LatestFrameHeight = observation.Value.Value.LatestFrameHeight;
        OperatorMessage = null;
    }

    public void Dispose()
    {
        _runtime = null;
        RaiseCommandState();
    }

    private async Task StartAsync()
        => await RunActionAsync(
            "start",
            runtime => runtime.StartCameraAsync(Definition.Id),
            CameraRuntimeState.Starting).ConfigureAwait(true);

    private async Task StopAsync()
        => await RunActionAsync(
            "stop",
            runtime => runtime.StopCameraAsync(Definition.Id),
            CameraRuntimeState.Stopped).ConfigureAwait(true);

    private async Task RunActionAsync(
        string action,
        Func<IWorkspaceRuntimeService, Task> operation,
        CameraRuntimeState successState)
    {
        var runtime = _runtime;
        if (runtime is null || IsBusy)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsBusy = true;
            OperatorMessage = null;
        }).ConfigureAwait(false);
        try
        {
            await operation(runtime).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() => State = successState).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(
                () => OperatorMessage = OperatorError.ForAction(Name, action, exception)).ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private void RaiseCommandState()
    {
        RaisePropertyChanged(nameof(CanStart));
        RaisePropertyChanged(nameof(CanStop));
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }
}
