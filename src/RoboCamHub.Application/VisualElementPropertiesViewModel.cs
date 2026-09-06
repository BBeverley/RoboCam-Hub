using System.Globalization;
using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public sealed class VisualElementPropertiesViewModel : ObservableObject
{
    private readonly ViewSceneElementDefinition _source;

    internal VisualElementPropertiesViewModel(ViewSceneElementDefinition source)
    {
        _source = source;
        X = source.X;
        Y = source.Y;
        Width = source.Width;
        Height = source.Height;
        RotationDegrees = source.RotationDegrees;
        ZOrder = source.ZOrder;
        Visible = source.Visible;
        FlipHorizontal = source.FlipHorizontal;
        FlipVertical = source.FlipVertical;
        switch (source)
        {
            case TextElementDefinition text:
                Text = text.Text;
                FontFamily = text.FontFamily;
                FontSize = text.FontSize;
                TextAlignment = text.Alignment;
                TextWeight = text.Weight;
                TextStyle = text.Style;
                PrimaryColor = FormatColor(text.TextColorRgba);
                SecondaryColor = text.BackgroundColorRgba is { } background ? FormatColor(background) : string.Empty;
                break;
            case ImageElementDefinition image:
                FitMode = image.FitMode;
                Opacity = image.Opacity;
                break;
            case ShapeElementDefinition rectangle:
                PrimaryColor = FormatColor(rectangle.FillColorRgba);
                SecondaryColor = rectangle.OutlineColorRgba is { } outline ? FormatColor(outline) : string.Empty;
                StrokeWidth = rectangle.OutlineWidth;
                Opacity = rectangle.Opacity;
                break;
            case FrameElementDefinition frame:
                PrimaryColor = FormatColor(frame.ColorRgba);
                StrokeWidth = frame.Thickness;
                Opacity = frame.Opacity;
                break;
        }
    }

    public string ElementId => _source.Id;
    public bool IsText => _source is TextElementDefinition;
    public bool IsImage => _source is ImageElementDefinition;
    public bool IsRectangle => _source is ShapeElementDefinition;
    public bool IsFrame => _source is FrameElementDefinition;
    public bool HasSecondaryColor => IsText || IsRectangle;
    public bool SupportsFlip => IsText || IsImage;
    public string TypeLabel => _source.GetType().Name.Replace("ElementDefinition", string.Empty, StringComparison.Ordinal);
    public IReadOnlyList<CameraElementFitMode> FitModes { get; } = Enum.GetValues<CameraElementFitMode>();
    public IReadOnlyList<TextElementAlignment> TextAlignments { get; } = Enum.GetValues<TextElementAlignment>();
    public IReadOnlyList<TextElementWeight> TextWeights { get; } = Enum.GetValues<TextElementWeight>();
    public IReadOnlyList<TextElementStyle> TextStyles { get; } = Enum.GetValues<TextElementStyle>();

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double RotationDegrees { get; set; }
    public int ZOrder { get; set; }
    public bool Visible { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Sans";
    public double FontSize { get; set; } = 48;
    public TextElementAlignment TextAlignment { get; set; }
    public TextElementWeight TextWeight { get; set; }
    public TextElementStyle TextStyle { get; set; }
    public CameraElementFitMode FitMode { get; set; } = CameraElementFitMode.Contain;
    public double Opacity { get; set; } = 1;
    public string PrimaryColor { get; set; } = "#FFFFFFFF";
    public string SecondaryColor { get; set; } = string.Empty;
    public double StrokeWidth { get; set; }

    internal ViewSceneElementDefinition ToDefinition()
        => _source switch
        {
            TextElementDefinition text => new TextElementDefinition(
                text.Id, Text, X, Y, Width, Height, ZOrder, FontFamily, FontSize,
                TextAlignment, TextWeight, TextStyle, ParseColor(PrimaryColor),
                ParseOptionalColor(SecondaryColor), RotationDegrees, FlipHorizontal,
                FlipVertical, Visible, text.Enabled),
            ImageElementDefinition image => new ImageElementDefinition(
                image.Id, image.AssetId, X, Y, Width, Height, ZOrder, FitMode, Opacity,
                RotationDegrees, FlipHorizontal, FlipVertical, Visible, image.Enabled),
            ShapeElementDefinition rectangle => new ShapeElementDefinition(
                rectangle.Id, X, Y, Width, Height, ZOrder, ParseColor(PrimaryColor),
                ParseOptionalColor(SecondaryColor), StrokeWidth, Opacity, RotationDegrees,
                Visible, rectangle.Enabled),
            FrameElementDefinition frame => new FrameElementDefinition(
                frame.Id, X, Y, Width, Height, ZOrder, ParseColor(PrimaryColor),
                StrokeWidth, Opacity, RotationDegrees, Visible, frame.Enabled),
            _ => throw new NotSupportedException("Camera properties use the camera property editor."),
        };

    private static string FormatColor(uint rgba) => $"#{rgba:X8}";

    private static uint ParseColor(string value)
    {
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }
        return value.Length == 8
               && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba)
            ? rgba
            : throw new FormatException("Colours must use #RRGGBBAA format.");
    }

    private static uint? ParseOptionalColor(string value)
        => string.IsNullOrWhiteSpace(value) ? null : ParseColor(value);
}
