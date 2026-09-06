namespace RoboCamHub.Domain;

public abstract class ViewSceneElementDefinition
{
    protected ViewSceneElementDefinition(
        string id,
        double x,
        double y,
        double width,
        double height,
        int zOrder,
        double rotationDegrees,
        bool flipHorizontal,
        bool flipVertical,
        bool visible,
        bool enabled)
    {
        Id = DefinitionValidation.StableId(id, nameof(id), "Scene element ID");
        X = ValidateCoordinate(x, nameof(x));
        Y = ValidateCoordinate(y, nameof(y));
        Width = ValidateExtent(width, nameof(width));
        Height = ValidateExtent(height, nameof(height));
        if (zOrder is < -1_000_000 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(zOrder));
        }

        if (!double.IsFinite(rotationDegrees) || rotationDegrees is < -360 or > 360)
        {
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        }

        ZOrder = zOrder;
        RotationDegrees = rotationDegrees;
        FlipHorizontal = flipHorizontal;
        FlipVertical = flipVertical;
        Visible = visible;
        Enabled = enabled;
    }

    public const double MaximumNormalizedMagnitude = 16;

    public string Id { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public int ZOrder { get; }

    public double RotationDegrees { get; }

    public bool FlipHorizontal { get; }

    public bool FlipVertical { get; }

    public bool Visible { get; }

    public bool Enabled { get; }

    private static double ValidateCoordinate(double value, string parameterName)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > MaximumNormalizedMagnitude)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static double ValidateExtent(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0 || value > MaximumNormalizedMagnitude)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}
