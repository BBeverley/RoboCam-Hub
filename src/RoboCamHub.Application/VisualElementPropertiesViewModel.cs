using System.Globalization;
using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public sealed class VisualElementPropertiesViewModel : ObservableObject
{
    private readonly ViewSceneElementDefinition _source;
    private TextElementAlignment _textAlignment;
    private TextElementVerticalAlignment _textVerticalAlignment;
    private TextElementWeight _textWeight;
    private TextElementStyle _textStyle;
    private bool _underline;
    private bool _secondaryColorEnabled;

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
                TextVerticalAlignment = text.VerticalAlignment;
                TextWeight = text.Weight;
                TextStyle = text.Style;
                Underline = text.Underline;
                PrimaryColor = FormatColor(text.TextColorRgba);
                SecondaryColor = text.BackgroundColorRgba is { } background ? FormatColor(background) : string.Empty;
                SecondaryColorEnabled = text.BackgroundColorRgba.HasValue;
                break;
            case ImageElementDefinition image:
                FitMode = image.FitMode;
                Opacity = image.Opacity;
                break;
            case ShapeElementDefinition rectangle:
                PrimaryColor = FormatColor(rectangle.FillColorRgba);
                SecondaryColor = rectangle.OutlineColorRgba is { } outline ? FormatColor(outline) : string.Empty;
                SecondaryColorEnabled = rectangle.OutlineColorRgba.HasValue;
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
    public bool HasPrimaryColor => IsText || IsRectangle || IsFrame;
    public bool HasSecondaryColor => IsText || IsRectangle;
    public string PrimaryColorLabel => IsText ? "Text colour" : IsRectangle ? "Fill colour" : "Frame colour";
    public string SecondaryColorLabel => IsText ? "Background colour" : "Outline colour";
    public bool SupportsFlip => IsText || IsImage;
    public string TypeLabel => _source.GetType().Name.Replace("ElementDefinition", string.Empty, StringComparison.Ordinal);
    public IReadOnlyList<CameraElementFitMode> FitModes { get; } = Enum.GetValues<CameraElementFitMode>();

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
    public TextElementAlignment TextAlignment
    {
        get => _textAlignment;
        set
        {
            if (SetProperty(ref _textAlignment, value))
            {
                RaisePropertyChanged(nameof(AlignLeft));
                RaisePropertyChanged(nameof(AlignCenter));
                RaisePropertyChanged(nameof(AlignRight));
            }
        }
    }
    public TextElementVerticalAlignment TextVerticalAlignment
    {
        get => _textVerticalAlignment;
        set
        {
            if (SetProperty(ref _textVerticalAlignment, value))
            {
                RaisePropertyChanged(nameof(AlignTop));
                RaisePropertyChanged(nameof(AlignMiddle));
                RaisePropertyChanged(nameof(AlignBottom));
            }
        }
    }
    public TextElementWeight TextWeight
    {
        get => _textWeight;
        set
        {
            if (SetProperty(ref _textWeight, value))
            {
                RaisePropertyChanged(nameof(IsBold));
            }
        }
    }
    public TextElementStyle TextStyle
    {
        get => _textStyle;
        set
        {
            if (SetProperty(ref _textStyle, value))
            {
                RaisePropertyChanged(nameof(IsItalic));
            }
        }
    }
    public bool Underline
    {
        get => _underline;
        set
        {
            if (SetProperty(ref _underline, value))
            {
                RaisePropertyChanged(nameof(IsUnderline));
            }
        }
    }
    public bool AlignLeft { get => TextAlignment == TextElementAlignment.Left; set => SetHorizontalAlignment(value, TextElementAlignment.Left, nameof(AlignLeft)); }
    public bool AlignCenter { get => TextAlignment == TextElementAlignment.Center; set => SetHorizontalAlignment(value, TextElementAlignment.Center, nameof(AlignCenter)); }
    public bool AlignRight { get => TextAlignment == TextElementAlignment.Right; set => SetHorizontalAlignment(value, TextElementAlignment.Right, nameof(AlignRight)); }
    public bool AlignTop { get => TextVerticalAlignment == TextElementVerticalAlignment.Top; set => SetVerticalAlignment(value, TextElementVerticalAlignment.Top, nameof(AlignTop)); }
    public bool AlignMiddle { get => TextVerticalAlignment == TextElementVerticalAlignment.Center; set => SetVerticalAlignment(value, TextElementVerticalAlignment.Center, nameof(AlignMiddle)); }
    public bool AlignBottom { get => TextVerticalAlignment == TextElementVerticalAlignment.Bottom; set => SetVerticalAlignment(value, TextElementVerticalAlignment.Bottom, nameof(AlignBottom)); }
    public bool IsBold { get => TextWeight == TextElementWeight.Bold; set => TextWeight = value ? TextElementWeight.Bold : TextElementWeight.Normal; }
    public bool IsItalic { get => TextStyle == TextElementStyle.Italic; set => TextStyle = value ? TextElementStyle.Italic : TextElementStyle.Normal; }
    public bool IsUnderline { get => Underline; set => Underline = value; }
    public CameraElementFitMode FitMode { get; set; } = CameraElementFitMode.Contain;
    public double Opacity { get; set; } = 1;
    public string PrimaryColor { get; set; } = "#FFFFFFFF";
    public string SecondaryColor { get; set; } = string.Empty;
    public bool SecondaryColorEnabled
    {
        get => _secondaryColorEnabled;
        set => SetProperty(ref _secondaryColorEnabled, value);
    }
    public double StrokeWidth { get; set; }

    internal ViewSceneElementDefinition ToDefinition()
        => _source switch
        {
            TextElementDefinition text => new TextElementDefinition(
                text.Id, Text, X, Y, Width, Height, ZOrder, FontFamily, FontSize,
                TextAlignment, TextWeight, TextStyle, ParseColor(PrimaryColor),
                SecondaryColorEnabled ? ParseColor(SecondaryColor) : null,
                RotationDegrees, FlipHorizontal,
                FlipVertical, Visible, text.Enabled, TextVerticalAlignment, Underline),
            ImageElementDefinition image => new ImageElementDefinition(
                image.Id, image.AssetId, X, Y, Width, Height, ZOrder, FitMode, Opacity,
                RotationDegrees, FlipHorizontal, FlipVertical, Visible, image.Enabled),
            ShapeElementDefinition rectangle => new ShapeElementDefinition(
                rectangle.Id, X, Y, Width, Height, ZOrder, ParseColor(PrimaryColor),
                SecondaryColorEnabled ? ParseColor(SecondaryColor) : null,
                StrokeWidth, Opacity, RotationDegrees,
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

    private void SetHorizontalAlignment(bool selected, TextElementAlignment alignment, string propertyName)
    {
        if (selected)
        {
            TextAlignment = alignment;
        }
        else
        {
            RaisePropertyChanged(propertyName);
        }
    }

    private void SetVerticalAlignment(bool selected, TextElementVerticalAlignment alignment, string propertyName)
    {
        if (selected)
        {
            TextVerticalAlignment = alignment;
        }
        else
        {
            RaisePropertyChanged(propertyName);
        }
    }
}
