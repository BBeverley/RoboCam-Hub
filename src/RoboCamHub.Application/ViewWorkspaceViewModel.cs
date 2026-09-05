using System.Collections.ObjectModel;
using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public sealed class ViewWorkspaceViewModel : ObservableObject, IDisposable
{
    private ViewRuntimeState _state = ViewRuntimeState.Stopped;
    private string? _operatorMessage;
    private uint _renderFpsMilli;

    public ViewWorkspaceViewModel(
        ViewDefinition definition,
        ObservableCollection<CameraItemViewModel> cameras,
        IWorkspaceRuntimeService runtime,
        IUiDispatcher dispatcher)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Slots = new ObservableCollection<ViewSlotViewModel>(
            Enumerable.Range(0, ViewDefinition.SlotCount)
                .Select(slotIndex => new ViewSlotViewModel(
                    (uint)slotIndex,
                    definition.GetCameraId(slotIndex),
                    cameras,
                    runtime,
                    dispatcher)));
    }

    public ViewDefinition Definition { get; }

    public string Name => Definition.Name;

    public ObservableCollection<ViewSlotViewModel> Slots { get; }

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
        if (snapshot.View.IsSuccess)
        {
            State = snapshot.View.Value!.Value.State;
            RenderFpsMilli = snapshot.View.Value.Value.RenderFpsMilli;
            OperatorMessage = null;
        }
        else
        {
            OperatorMessage = snapshot.View.ErrorMessage;
        }

        foreach (var slot in Slots)
        {
            if (snapshot.ViewSources.TryGetValue(slot.SlotIndex, out var observation))
            {
                slot.ApplyStatus(observation);
            }
        }
    }

    public void Dispose()
    {
        foreach (var slot in Slots)
        {
            slot.Dispose();
        }
    }
}
