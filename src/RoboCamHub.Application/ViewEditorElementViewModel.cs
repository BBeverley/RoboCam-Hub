using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public sealed class ViewEditorElementViewModel : ObservableObject
{
    private CameraElementDefinition _definition;
    private readonly CameraItemViewModel? _camera;
    private bool _isSelected;

    internal ViewEditorElementViewModel(
        CameraElementDefinition definition,
        string cameraName,
        CameraItemViewModel? camera = null)
    {
        _definition = definition;
        _camera = camera;
        CameraName = cameraName;
    }

    public CameraElementDefinition Definition
    {
        get => _definition;
        internal set
        {
            if (SetProperty(ref _definition, value))
            {
                RaisePropertyChanged(nameof(X));
                RaisePropertyChanged(nameof(Y));
                RaisePropertyChanged(nameof(Width));
                RaisePropertyChanged(nameof(Height));
                RaisePropertyChanged(nameof(RotationDegrees));
                RaisePropertyChanged(nameof(ZOrder));
                RaisePropertyChanged(nameof(IsVisibleOnCanvas));
                RaisePropertyChanged(nameof(Geometry));
            }
        }
    }

    public string Id => Definition.Id;

    public string CameraId => Definition.CameraId;

    public string CameraName { get; }

    public double X => Definition.X;

    public double Y => Definition.Y;

    public double Width => Definition.Width;

    public double Height => Definition.Height;

    public double RotationDegrees => Definition.RotationDegrees;

    public int ZOrder => Definition.ZOrder;

    public bool IsVisibleOnCanvas => Definition.Visible && Definition.Enabled;

    public EditorElementGeometry Geometry
        => ViewEditorGeometry.Calculate(
            Definition,
            _camera?.LatestFrameWidth ?? 0,
            _camera?.LatestFrameHeight ?? 0);

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    internal void NotifySourceGeometryChanged() => RaisePropertyChanged(nameof(Geometry));
}
