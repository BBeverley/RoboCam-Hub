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

    public AsyncCommand StartCommand { get; }

    public AsyncCommand StopCommand { get; }

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
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }
}
