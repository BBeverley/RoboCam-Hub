namespace RoboCamHub.NativeInterop;

public enum NativeCameraElementFitMode : uint
{
    Stretch = 0,
    Contain = 1,
    Cover = 2,
}

public readonly record struct NativeCameraElementConfig(
    string ElementId,
    string CameraId,
    double X,
    double Y,
    double Width,
    double Height,
    int ZOrder = 0,
    double CropLeft = 0,
    double CropTop = 0,
    double CropRight = 0,
    double CropBottom = 0,
    double RotationDegrees = 0,
    bool FlipHorizontal = false,
    bool FlipVertical = false,
    bool Visible = true,
    bool Enabled = true,
    NativeCameraElementFitMode FitMode = NativeCameraElementFitMode.Stretch);
