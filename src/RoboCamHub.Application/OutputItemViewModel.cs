using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public sealed class OutputItemViewModel : ObservableObject, IDisposable
{
    private IWorkspaceRuntimeService? _runtime;
    private readonly IUiDispatcher _dispatcher;
    private OutputRuntimeState _state = OutputRuntimeState.Stopped;
    private bool _isBusy;
    private string? _operatorMessage;
    private bool _receiverCountKnown;
    private uint _receiverCount;
    private uint _sendFpsMilli;
    private ulong _latestSentFrameAgeMs = ulong.MaxValue;
    private ulong _droppedOrSkippedFrameCount;
    private uint _averageSendDurationUs;
    private uint _p95SendDurationUs;

    public OutputItemViewModel(
        OutputDefinition definition,
        string viewName,
        IWorkspaceRuntimeService runtime,
        IUiDispatcher dispatcher)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ViewName = viewName;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        StartCommand = new AsyncCommand(StartAsync, () => CanStart);
        StopCommand = new AsyncCommand(StopAsync, () => CanStop);
        RestartCommand = new AsyncCommand(RestartAsync, () => CanRestart);
    }

    public OutputDefinition Definition { get; }

    public string Name => Definition.Name;

    public string NdiSourceName => Definition.NdiSourceName;

    public string ViewName { get; }

    public string ViewLabel => $"View: {ViewName}";

    public OutputRuntimeState State
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
        OutputRuntimeState.Starting => "Starting",
        OutputRuntimeState.Running => "Running",
        OutputRuntimeState.WaitingForViewFrame => "Waiting for View Frame",
        OutputRuntimeState.Failed => "Failed",
        _ => "Stopped",
    };

    public string HealthIcon => State switch
    {
        OutputRuntimeState.Running => "●",
        OutputRuntimeState.Starting or OutputRuntimeState.WaitingForViewFrame => "◐",
        OutputRuntimeState.Failed => "✕",
        _ => "○",
    };

    public string HealthColor => State switch
    {
        OutputRuntimeState.Running => "#45C782",
        OutputRuntimeState.Starting or OutputRuntimeState.WaitingForViewFrame => "#F5B84B",
        OutputRuntimeState.Failed => "#F06A6A",
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

    public bool ReceiverCountKnown
    {
        get => _receiverCountKnown;
        private set => SetProperty(ref _receiverCountKnown, value);
    }

    public uint ReceiverCount
    {
        get => _receiverCount;
        private set
        {
            if (SetProperty(ref _receiverCount, value))
            {
                RaisePropertyChanged(nameof(ReceiverCountText));
            }
        }
    }

    public string ReceiverCountText => $"Receivers: {ReceiverCount}";

    public uint SendFpsMilli
    {
        get => _sendFpsMilli;
        private set
        {
            if (SetProperty(ref _sendFpsMilli, value))
            {
                RaisePropertyChanged(nameof(SendFpsText));
            }
        }
    }

    public string SendFpsText => $"Send: {SendFpsMilli / 1000.0:F1} fps";

    public ulong LatestSentFrameAgeMs
    {
        get => _latestSentFrameAgeMs;
        private set
        {
            if (SetProperty(ref _latestSentFrameAgeMs, value))
            {
                RaisePropertyChanged(nameof(FrameAgeText));
            }
        }
    }

    public string FrameAgeText => LatestSentFrameAgeMs == ulong.MaxValue
        ? "Age —"
        : $"Age {LatestSentFrameAgeMs} ms";

    public ulong DroppedOrSkippedFrameCount
    {
        get => _droppedOrSkippedFrameCount;
        private set
        {
            if (SetProperty(ref _droppedOrSkippedFrameCount, value))
            {
                RaisePropertyChanged(nameof(DroppedFramesText));
            }
        }
    }

    public string DroppedFramesText => $"Skipped {DroppedOrSkippedFrameCount}";

    public uint AverageSendDurationUs
    {
        get => _averageSendDurationUs;
        private set
        {
            if (SetProperty(ref _averageSendDurationUs, value))
            {
                RaisePropertyChanged(nameof(SendDurationText));
            }
        }
    }

    public uint P95SendDurationUs
    {
        get => _p95SendDurationUs;
        private set
        {
            if (SetProperty(ref _p95SendDurationUs, value))
            {
                RaisePropertyChanged(nameof(SendDurationText));
            }
        }
    }

    public string SendDurationText => $"Send avg/p95 {AverageSendDurationUs}/{P95SendDurationUs} µs";

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
        && State is OutputRuntimeState.Stopped or OutputRuntimeState.Failed;

    public bool CanStop => _runtime is not null
        && !IsBusy
        && State is not OutputRuntimeState.Stopped;

    public bool CanRestart => _runtime is not null
        && Definition.Enabled
        && !IsBusy
        && State is not OutputRuntimeState.Starting;

    public AsyncCommand StartCommand { get; }

    public AsyncCommand StopCommand { get; }

    public AsyncCommand RestartCommand { get; }

    internal void ApplyStatus(RuntimeObservation<OutputRuntimeStatus> observation)
    {
        if (!observation.IsSuccess)
        {
            OperatorMessage = observation.ErrorMessage;
            return;
        }

        var status = observation.Value!.Value;
        State = status.State;
        ReceiverCountKnown = status.ReceiverCountKnown;
        ReceiverCount = status.ReceiverCount;
        SendFpsMilli = status.SendFpsMilli;
        LatestSentFrameAgeMs = status.LatestSentFrameAgeMs;
        DroppedOrSkippedFrameCount = status.DroppedOrSkippedFrameCount;
        AverageSendDurationUs = status.AverageSendDurationUs;
        P95SendDurationUs = status.P95SendDurationUs;
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
            runtime => runtime.StartOutputAsync(Definition.Id),
            OutputRuntimeState.Starting).ConfigureAwait(true);

    private async Task StopAsync()
        => await RunActionAsync(
            "stop",
            runtime => runtime.StopOutputAsync(Definition.Id),
            OutputRuntimeState.Stopped).ConfigureAwait(true);

    private async Task RestartAsync()
        => await RunActionAsync(
            "restart",
            runtime => runtime.RestartOutputAsync(Definition.Id),
            OutputRuntimeState.Starting).ConfigureAwait(true);

    private async Task RunActionAsync(
        string action,
        Func<IWorkspaceRuntimeService, Task> operation,
        OutputRuntimeState successState)
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
        RaisePropertyChanged(nameof(CanRestart));
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RestartCommand.RaiseCanExecuteChanged();
    }
}
