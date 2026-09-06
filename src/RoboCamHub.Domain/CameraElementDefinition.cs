namespace RoboCamHub.Domain;

public enum CameraElementFitMode : uint
{
    Stretch = 0,
    Contain = 1,
    Cover = 2,
}

public sealed class CameraElementDefinition : ViewSceneElementDefinition
{
    public CameraElementDefinition(
        string id,
        string cameraId,
        double x,
        double y,
        double width,
        double height,
        int zOrder = 0,
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
        : base(
            id,
            x,
            y,
            width,
            height,
            zOrder,
            rotationDegrees,
            flipHorizontal,
            flipVertical,
            visible,
            enabled)
    {
        CameraId = DefinitionValidation.StableId(cameraId, nameof(cameraId), "Camera element logical camera ID");
        CropLeft = ValidateCrop(cropLeft, nameof(cropLeft));
        CropTop = ValidateCrop(cropTop, nameof(cropTop));
        CropRight = ValidateCrop(cropRight, nameof(cropRight));
        CropBottom = ValidateCrop(cropBottom, nameof(cropBottom));
        if (CropLeft + CropRight >= 1)
        {
            throw new ArgumentException("Horizontal crop must leave part of the source visible.");
        }

        if (CropTop + CropBottom >= 1)
        {
            throw new ArgumentException("Vertical crop must leave part of the source visible.");
        }

        if (!Enum.IsDefined(fitMode))
        {
            throw new ArgumentOutOfRangeException(nameof(fitMode));
        }

        FitMode = fitMode;
    }

    public string CameraId { get; }

    public double CropLeft { get; }

    public double CropTop { get; }

    public double CropRight { get; }

    public double CropBottom { get; }

    public CameraElementFitMode FitMode { get; }

    private static double ValidateCrop(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}
