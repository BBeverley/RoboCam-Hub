using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public sealed class CameraElementPropertiesViewModel : ObservableObject
{
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private double _cropLeft;
    private double _cropTop;
    private double _cropRight;
    private double _cropBottom;
    private double _rotationDegrees;
    private int _zOrder;
    private bool _flipHorizontal;
    private bool _flipVertical;
    private bool _visible;
    private CameraElementFitMode _fitMode;

    internal CameraElementPropertiesViewModel(CameraElementDefinition definition)
    {
        ElementId = definition.Id;
        CameraId = definition.CameraId;
        _x = definition.X;
        _y = definition.Y;
        _width = definition.Width;
        _height = definition.Height;
        _cropLeft = definition.CropLeft;
        _cropTop = definition.CropTop;
        _cropRight = definition.CropRight;
        _cropBottom = definition.CropBottom;
        _rotationDegrees = definition.RotationDegrees;
        _zOrder = definition.ZOrder;
        _flipHorizontal = definition.FlipHorizontal;
        _flipVertical = definition.FlipVertical;
        _visible = definition.Visible;
        _fitMode = definition.FitMode;
    }

    public string ElementId { get; }

    public string CameraId { get; }

    public IReadOnlyList<CameraElementFitMode> FitModes { get; } = Enum.GetValues<CameraElementFitMode>();

    public double X { get => _x; set => SetProperty(ref _x, value); }

    public double Y { get => _y; set => SetProperty(ref _y, value); }

    public double Width { get => _width; set => SetProperty(ref _width, value); }

    public double Height { get => _height; set => SetProperty(ref _height, value); }

    public double CropLeft { get => _cropLeft; set => SetProperty(ref _cropLeft, value); }

    public double CropTop { get => _cropTop; set => SetProperty(ref _cropTop, value); }

    public double CropRight { get => _cropRight; set => SetProperty(ref _cropRight, value); }

    public double CropBottom { get => _cropBottom; set => SetProperty(ref _cropBottom, value); }

    public double RotationDegrees { get => _rotationDegrees; set => SetProperty(ref _rotationDegrees, value); }

    public int ZOrder { get => _zOrder; set => SetProperty(ref _zOrder, value); }

    public bool FlipHorizontal { get => _flipHorizontal; set => SetProperty(ref _flipHorizontal, value); }

    public bool FlipVertical { get => _flipVertical; set => SetProperty(ref _flipVertical, value); }

    public bool Visible { get => _visible; set => SetProperty(ref _visible, value); }

    public CameraElementFitMode FitMode { get => _fitMode; set => SetProperty(ref _fitMode, value); }

    internal CameraElementDefinition ToDefinition(CameraElementDefinition applied)
        => new(
            applied.Id,
            applied.CameraId,
            X,
            Y,
            Width,
            Height,
            ZOrder,
            CropLeft,
            CropTop,
            CropRight,
            CropBottom,
            RotationDegrees,
            FlipHorizontal,
            FlipVertical,
            Visible,
            applied.Enabled,
            FitMode);
}
