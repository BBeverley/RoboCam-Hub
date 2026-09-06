using System.Collections.ObjectModel;

namespace RoboCamHub.Domain;

public sealed class ViewTemplateDefinition
{
    private readonly ReadOnlyCollection<ViewTemplateSlotDefinition> _slots;

    public ViewTemplateDefinition(
        string id,
        string name,
        IEnumerable<ViewTemplateSlotDefinition> slots)
    {
        Id = DefinitionValidation.StableId(id, nameof(id), "View template ID");
        Name = DefinitionValidation.Required(name, nameof(name), "View template name");
        ArgumentNullException.ThrowIfNull(slots);

        var definitions = slots.ToArray();
        if (definitions.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(slots), "A View template supports at most 256 slots.");
        }
        if (definitions.Any(slot => slot is null))
        {
            throw new ArgumentException("View template slots must not contain null values.", nameof(slots));
        }

        var duplicate = definitions
            .GroupBy(slot => slot.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"View template slot ID '{duplicate.Key}' is duplicated.",
                nameof(slots));
        }

        _slots = Array.AsReadOnly(definitions);
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<ViewTemplateSlotDefinition> Slots => _slots;
}

public sealed class ViewTemplateSlotDefinition
{
    public ViewTemplateSlotDefinition(
        string id,
        double x,
        double y,
        double width,
        double height,
        int zOrder = 0,
        string? displayLabel = null,
        double cropLeft = 0,
        double cropTop = 0,
        double cropRight = 0,
        double cropBottom = 0,
        double rotationDegrees = 0,
        bool flipHorizontal = false,
        bool flipVertical = false,
        bool visible = true,
        bool enabled = true,
        CameraElementFitMode fitMode = CameraElementFitMode.Stretch)
    {
        Id = DefinitionValidation.StableId(id, nameof(id), "View template slot ID");
        DisplayLabel = displayLabel is null
            ? null
            : DefinitionValidation.Required(displayLabel, nameof(displayLabel), "View template slot label");
        X = ValidateCoordinate(x, nameof(x));
        Y = ValidateCoordinate(y, nameof(y));
        Width = ValidateExtent(width, nameof(width));
        Height = ValidateExtent(height, nameof(height));
        if (zOrder is < -1_000_000 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(zOrder));
        }
        CropLeft = ValidateCrop(cropLeft, nameof(cropLeft));
        CropTop = ValidateCrop(cropTop, nameof(cropTop));
        CropRight = ValidateCrop(cropRight, nameof(cropRight));
        CropBottom = ValidateCrop(cropBottom, nameof(cropBottom));
        if (CropLeft + CropRight >= 1)
        {
            throw new ArgumentException("Horizontal crop must leave part of the template slot visible.");
        }
        if (CropTop + CropBottom >= 1)
        {
            throw new ArgumentException("Vertical crop must leave part of the template slot visible.");
        }
        if (!double.IsFinite(rotationDegrees) || rotationDegrees is < -360 or > 360)
        {
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        }
        if (!Enum.IsDefined(fitMode))
        {
            throw new ArgumentOutOfRangeException(nameof(fitMode));
        }

        ZOrder = zOrder;
        RotationDegrees = rotationDegrees;
        FlipHorizontal = flipHorizontal;
        FlipVertical = flipVertical;
        Visible = visible;
        Enabled = enabled;
        FitMode = fitMode;
    }

    public string Id { get; }

    public string? DisplayLabel { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public int ZOrder { get; }

    public double CropLeft { get; }

    public double CropTop { get; }

    public double CropRight { get; }

    public double CropBottom { get; }

    public double RotationDegrees { get; }

    public bool FlipHorizontal { get; }

    public bool FlipVertical { get; }

    public bool Visible { get; }

    public bool Enabled { get; }

    public CameraElementFitMode FitMode { get; }

    private static double ValidateCoordinate(double value, string parameterName)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > ViewSceneElementDefinition.MaximumNormalizedMagnitude)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }

    private static double ValidateExtent(double value, string parameterName)
    {
        if (!double.IsFinite(value)
            || value <= 0
            || value > ViewSceneElementDefinition.MaximumNormalizedMagnitude)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }

    private static double ValidateCrop(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }
}
