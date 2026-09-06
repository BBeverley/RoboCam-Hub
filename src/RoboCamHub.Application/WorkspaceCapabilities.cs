namespace RoboCamHub.Application;

public enum WorkspaceMode
{
    Edit,
    Show,
}

public sealed class WorkspaceCapabilities : ObservableObject
{
    private WorkspaceMode _mode = WorkspaceMode.Edit;

    public WorkspaceMode Mode
    {
        get => _mode;
        internal set
        {
            if (!SetProperty(ref _mode, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(CanEditScene));
            RaisePropertyChanged(nameof(CanCreateView));
            RaisePropertyChanged(nameof(CanEditCameraAssignments));
            RaisePropertyChanged(nameof(CanConfigureOutputs));
        }
    }

    public bool CanEditScene => Mode == WorkspaceMode.Edit;

    public bool CanCreateView => Mode == WorkspaceMode.Edit;

    public bool CanEditCameraAssignments => Mode == WorkspaceMode.Edit;

    public bool CanConfigureOutputs => Mode == WorkspaceMode.Edit;

    public bool CanOperateOutputs => true;

    public bool CanSwitchPreviewView => true;

    public bool CanUseFullscreen => true;
}
