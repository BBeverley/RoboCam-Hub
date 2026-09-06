namespace RoboCamHub.Domain;

public enum TextElementAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
}

public enum TextElementWeight
{
    Normal = 0,
    Bold = 1,
}

public enum TextElementStyle
{
    Normal = 0,
    Italic = 1,
}

public sealed class TextElementDefinition : ViewSceneElementDefinition
{
    public TextElementDefinition(
        string id,
        string text,
        double x,
        double y,
        double width,
        double height,
        int zOrder,
        string fontFamily = "Sans",
        double fontSize = 48,
        TextElementAlignment alignment = TextElementAlignment.Left,
        TextElementWeight weight = TextElementWeight.Normal,
        TextElementStyle style = TextElementStyle.Normal,
        uint textColorRgba = 0xFFFFFFFF,
        uint? backgroundColorRgba = null,
        double rotationDegrees = 0,
        bool flipHorizontal = false,
        bool flipVertical = false,
        bool visible = true,
        bool enabled = true)
        : base(id, x, y, width, height, zOrder, rotationDegrees, flipHorizontal, flipVertical, visible, enabled)
    {
        Text = DefinitionValidation.Required(text, nameof(text), "Text content");
        FontFamily = DefinitionValidation.Required(fontFamily, nameof(fontFamily), "Font family");
        if (!double.IsFinite(fontSize) || fontSize is < 1 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }
        if (!Enum.IsDefined(alignment) || !Enum.IsDefined(weight) || !Enum.IsDefined(style))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }
        FontSize = fontSize;
        Alignment = alignment;
        Weight = weight;
        Style = style;
        TextColorRgba = textColorRgba;
        BackgroundColorRgba = backgroundColorRgba;
    }

    public string Text { get; }
    public string FontFamily { get; }
    public double FontSize { get; }
    public TextElementAlignment Alignment { get; }
    public TextElementWeight Weight { get; }
    public TextElementStyle Style { get; }
    public uint TextColorRgba { get; }
    public uint? BackgroundColorRgba { get; }
}

public sealed class ImageElementDefinition : ViewSceneElementDefinition
{
    public ImageElementDefinition(
        string id,
        string assetId,
        double x,
        double y,
        double width,
        double height,
        int zOrder,
        CameraElementFitMode fitMode = CameraElementFitMode.Contain,
        double opacity = 1,
        double rotationDegrees = 0,
        bool flipHorizontal = false,
        bool flipVertical = false,
        bool visible = true,
        bool enabled = true)
        : base(id, x, y, width, height, zOrder, rotationDegrees, flipHorizontal, flipVertical, visible, enabled)
    {
        AssetId = DefinitionValidation.StableId(assetId, nameof(assetId), "Asset ID");
        if (!Enum.IsDefined(fitMode))
        {
            throw new ArgumentOutOfRangeException(nameof(fitMode));
        }
        Opacity = ValidateOpacity(opacity);
        FitMode = fitMode;
    }

    public string AssetId { get; }
    public CameraElementFitMode FitMode { get; }
    public double Opacity { get; }

    private static double ValidateOpacity(double value)
        => double.IsFinite(value) && value is >= 0 and <= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
}

public sealed class ShapeElementDefinition : ViewSceneElementDefinition
{
    public ShapeElementDefinition(
        string id,
        double x,
        double y,
        double width,
        double height,
        int zOrder,
        uint fillColorRgba,
        uint? outlineColorRgba = null,
        double outlineWidth = 0,
        double opacity = 1,
        double rotationDegrees = 0,
        bool visible = true,
        bool enabled = true)
        : base(id, x, y, width, height, zOrder, rotationDegrees, false, false, visible, enabled)
    {
        ValidateVisualValues(outlineWidth, opacity);
        FillColorRgba = fillColorRgba;
        OutlineColorRgba = outlineColorRgba;
        OutlineWidth = outlineWidth;
        Opacity = opacity;
    }

    public uint FillColorRgba { get; }
    public uint? OutlineColorRgba { get; }
    public double OutlineWidth { get; }
    public double Opacity { get; }

    internal static void ValidateVisualValues(double outlineWidth, double opacity)
    {
        if (!double.IsFinite(outlineWidth) || outlineWidth is < 0 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(outlineWidth));
        }
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity));
        }
    }
}

public sealed class FrameElementDefinition : ViewSceneElementDefinition
{
    public FrameElementDefinition(
        string id,
        double x,
        double y,
        double width,
        double height,
        int zOrder,
        uint colorRgba,
        double thickness = 8,
        double opacity = 1,
        double rotationDegrees = 0,
        bool visible = true,
        bool enabled = true)
        : base(id, x, y, width, height, zOrder, rotationDegrees, false, false, visible, enabled)
    {
        ShapeElementDefinition.ValidateVisualValues(thickness, opacity);
        if (thickness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness));
        }
        ColorRgba = colorRgba;
        Thickness = thickness;
        Opacity = opacity;
    }

    public uint ColorRgba { get; }
    public double Thickness { get; }
    public double Opacity { get; }
}
