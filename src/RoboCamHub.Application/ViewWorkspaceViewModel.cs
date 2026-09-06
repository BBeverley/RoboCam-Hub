using System.Collections.ObjectModel;
using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public sealed class ViewWorkspaceViewModel : ObservableObject, IDisposable
{
    private ViewRuntimeState _state = ViewRuntimeState.Stopped;
    private string? _operatorMessage;
    private uint _renderFpsMilli;
    private uint _outputConsumerCount;

    public ViewWorkspaceViewModel(
        ViewDefinition definition,
        ObservableCollection<CameraItemViewModel> cameras,
        IWorkspaceRuntimeService runtime,
        IUiDispatcher dispatcher)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Editor = new ViewEditorViewModel(
            definition,
            cameras,
            runtime,
            dispatcher,
            appliedDefinition => Definition = appliedDefinition);
        Slots = new ObservableCollection<ViewSlotViewModel>(
            Enumerable.Range(0, ViewDefinition.SlotCount)
                .Select(slotIndex => new ViewSlotViewModel(
                    definition.Id,
                    (uint)slotIndex,
                    definition.GetCameraId(slotIndex),
                    cameras,
                    runtime,
                    dispatcher)));
    }

    public ViewDefinition Definition { get; private set; }

    public string Name => Definition.Name;

    public ObservableCollection<ViewSlotViewModel> Slots { get; }

    public ViewEditorViewModel Editor { get; }

    public ViewRuntimeState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                RaisePropertyChanged(nameof(StateText));
            }
        }
    }

    public string StateText => State == ViewRuntimeState.Running ? "Running" : "Stopped";

    public uint RenderFpsMilli
    {
        get => _renderFpsMilli;
        private set
        {
            if (SetProperty(ref _renderFpsMilli, value))
            {
                RaisePropertyChanged(nameof(RenderFpsText));
            }
        }
    }

    public string RenderFpsText => $"{RenderFpsMilli / 1000.0:F1} fps";

    public uint OutputConsumerCount
    {
        get => _outputConsumerCount;
        private set
        {
            if (SetProperty(ref _outputConsumerCount, value))
            {
                RaisePropertyChanged(nameof(OutputConsumerText));
            }
        }
    }

    public string OutputConsumerText => $"Outputs {OutputConsumerCount}";

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

    internal void ApplySnapshot(WorkspaceRuntimeSnapshot snapshot)
    {
        if (!snapshot.Views.TryGetValue(Definition.Id, out var viewObservation))
        {
            OperatorMessage = $"View '{Definition.Name}' status is temporarily unavailable.";
            return;
        }

        if (viewObservation.IsSuccess)
        {
            State = viewObservation.Value!.Value.State;
            RenderFpsMilli = viewObservation.Value.Value.RenderFpsMilli;
            OutputConsumerCount = viewObservation.Value.Value.OutputConsumerCount;
            OperatorMessage = null;
        }
        else
        {
            OperatorMessage = viewObservation.ErrorMessage;
        }

        if (!snapshot.ViewSources.TryGetValue(Definition.Id, out var sourceStatuses))
        {
            return;
        }
        foreach (var slot in Slots)
        {
            if (sourceStatuses.TryGetValue(slot.SlotIndex, out var observation))
            {
                slot.ApplyStatus(observation);
            }
        }
    }

    public void Dispose()
    {
        Editor.Dispose();
        foreach (var slot in Slots)
        {
            slot.Dispose();
        }
    }
}
