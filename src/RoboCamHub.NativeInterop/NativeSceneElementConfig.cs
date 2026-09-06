namespace RoboCamHub.NativeInterop;

public enum NativeSceneElementKind : uint { Camera, Text, Image, Rectangle, Frame }
public enum NativeTextAlignment : uint { Left, Center, Right }
public enum NativeTextVerticalAlignment : uint { Top, Center, Bottom }
public enum NativeTextWeight : uint { Normal, Bold }
public enum NativeTextStyle : uint { Normal, Italic }

public sealed record NativeSceneElementConfig(
    NativeSceneElementKind Kind,
    string ElementId,
    double X,
    double Y,
    double Width,
    double Height,
    int ZOrder,
    double RotationDegrees = 0,
    bool FlipHorizontal = false,
    bool FlipVertical = false,
    bool Visible = true,
    bool Enabled = true,
    double Opacity = 1,
    string? CameraId = null,
    string? ImageAssetId = null,
    string? ImageSource = null,
    string? Text = null,
    string? FontFamily = null,
    NativeCameraElementFitMode FitMode = NativeCameraElementFitMode.Stretch,
    uint PrimaryRgba = 0,
    uint SecondaryRgba = 0,
    bool SecondaryEnabled = false,
    double StrokeWidth = 0,
    double FontSize = 0,
    NativeTextAlignment TextAlignment = NativeTextAlignment.Left,
    NativeTextWeight TextWeight = NativeTextWeight.Normal,
    NativeTextStyle TextStyle = NativeTextStyle.Normal,
    NativeTextVerticalAlignment TextVerticalAlignment = NativeTextVerticalAlignment.Top,
    bool TextUnderline = false);
