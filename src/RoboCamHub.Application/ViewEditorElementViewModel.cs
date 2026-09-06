using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public sealed class ViewEditorElementViewModel : ObservableObject
{
    private ViewSceneElementDefinition _definition;
    private readonly CameraItemViewModel? _camera;
    private readonly AssetDefinition? _asset;
    private bool _isSelected;

    internal ViewEditorElementViewModel(
        ViewSceneElementDefinition definition,
        string displayName,
        CameraItemViewModel? camera = null,
        AssetDefinition? asset = null)
    {
        _definition = definition;
        _camera = camera;
        _asset = asset;
        DisplayName = displayName;
    }

    public ViewSceneElementDefinition Definition
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

    public string? CameraId => (Definition as CameraElementDefinition)?.CameraId;

    public string DisplayName { get; }

    public string KindLabel => Definition switch
    {
        CameraElementDefinition => "Camera",
        TextElementDefinition => "Text",
        ImageElementDefinition => "Image",
        ShapeElementDefinition => "Rectangle",
        FrameElementDefinition => "Frame",
        _ => "Element",
    };

    public string SelectionLabel => $"{KindLabel}: {DisplayName} · {Id}";

    public string BrushKey => CameraId ?? $"{KindLabel}:{Id}";

    public double X => Definition.X;

    public double Y => Definition.Y;

    public double Width => Definition.Width;

    public double Height => Definition.Height;

    public double RotationDegrees => Definition.RotationDegrees;

    public double CropLeft => (Definition as CameraElementDefinition)?.CropLeft ?? 0;

    public double CropRight => (Definition as CameraElementDefinition)?.CropRight ?? 0;

    public int ZOrder => Definition.ZOrder;

    public bool IsVisibleOnCanvas => Definition.Visible && Definition.Enabled;

    public EditorElementGeometry Geometry
        => ViewEditorGeometry.Calculate(
            Definition,
            _camera?.LatestFrameWidth ?? _asset?.PixelWidth ?? 0,
            _camera?.LatestFrameHeight ?? _asset?.PixelHeight ?? 0);

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    public bool HitTest(EditorPoint point)
    {
        if (!Geometry.ContainsVisible(point))
        {
            return false;
        }
        if (Definition is not FrameElementDefinition frame)
        {
            return true;
        }

        var local = ViewEditorGeometry.InverseRotate(point, Geometry.DestinationBounds.Centre, Definition.RotationDegrees);
        var horizontalThickness = frame.Thickness / 1920d;
        var verticalThickness = frame.Thickness / 1080d;
        return local.X - Definition.X <= horizontalThickness
               || Definition.X + Definition.Width - local.X <= horizontalThickness
               || local.Y - Definition.Y <= verticalThickness
               || Definition.Y + Definition.Height - local.Y <= verticalThickness;
    }

    internal void NotifySourceGeometryChanged() => RaisePropertyChanged(nameof(Geometry));
}
