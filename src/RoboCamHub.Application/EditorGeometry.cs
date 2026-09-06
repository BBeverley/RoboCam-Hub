using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public readonly record struct EditorPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public readonly record struct EditorRectangle(double X, double Y, double Width, double Height)
{
    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public EditorPoint Centre => new(X + Width / 2, Y + Height / 2);
}

public readonly record struct EditorElementGeometry(
    EditorRectangle DestinationBounds,
    EditorRectangle VisibleBounds,
    double RotationDegrees)
{
    public bool HasTransparentContainerSpace
        => Math.Abs(DestinationBounds.X - VisibleBounds.X) > 1e-12
           || Math.Abs(DestinationBounds.Y - VisibleBounds.Y) > 1e-12
           || Math.Abs(DestinationBounds.Width - VisibleBounds.Width) > 1e-12
           || Math.Abs(DestinationBounds.Height - VisibleBounds.Height) > 1e-12;

    public IReadOnlyList<EditorPoint> DestinationCorners
        => ViewEditorGeometry.RotateCorners(DestinationBounds, DestinationBounds.Centre, RotationDegrees);

    public IReadOnlyList<EditorPoint> VisibleCorners
        => ViewEditorGeometry.RotateCorners(VisibleBounds, DestinationBounds.Centre, RotationDegrees);

    public IReadOnlyList<EditorPoint> ManipulationCorners => VisibleCorners;

    public bool ContainsVisible(EditorPoint point)
    {
        if (!point.IsFinite)
        {
            return false;
        }

        var local = ViewEditorGeometry.InverseRotate(point, DestinationBounds.Centre, RotationDegrees);
        return local.X >= VisibleBounds.Left
               && local.X <= VisibleBounds.Right
               && local.Y >= VisibleBounds.Top
               && local.Y <= VisibleBounds.Bottom;
    }
}

public static class ViewEditorGeometry
{
    public const double CanvasAspectRatio = 16d / 9d;

    public static EditorElementGeometry Calculate(
        CameraElementDefinition element,
        uint sourcePixelWidth,
        uint sourcePixelHeight)
    {
        ArgumentNullException.ThrowIfNull(element);

        var destination = new EditorRectangle(element.X, element.Y, element.Width, element.Height);
        var visible = destination;
        if (element.FitMode == CameraElementFitMode.Contain)
        {
            var sourceAspect = SourceAspectAfterCrop(element, sourcePixelWidth, sourcePixelHeight);
            var destinationAspect = element.Width * CanvasAspectRatio / element.Height;
            if (sourceAspect > destinationAspect)
            {
                var contentHeight = destinationAspect / sourceAspect;
                var visibleHeight = element.Height * contentHeight;
                visible = new EditorRectangle(
                    element.X,
                    element.Y + (element.Height - visibleHeight) / 2,
                    element.Width,
                    visibleHeight);
            }
            else
            {
                var contentWidth = sourceAspect / destinationAspect;
                var visibleWidth = element.Width * contentWidth;
                visible = new EditorRectangle(
                    element.X + (element.Width - visibleWidth) / 2,
                    element.Y,
                    visibleWidth,
                    element.Height);
            }
        }

        return new EditorElementGeometry(destination, visible, element.RotationDegrees);
    }

    public static EditorPoint Rotate(EditorPoint point, EditorPoint centre, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var deltaX = (point.X - centre.X) * CanvasAspectRatio;
        var deltaY = point.Y - centre.Y;
        return new EditorPoint(
            centre.X + (deltaX * Math.Cos(radians) - deltaY * Math.Sin(radians)) / CanvasAspectRatio,
            centre.Y + deltaX * Math.Sin(radians) + deltaY * Math.Cos(radians));
    }

    internal static EditorPoint InverseRotate(EditorPoint point, EditorPoint centre, double degrees)
        => Rotate(point, centre, -degrees);

    internal static IReadOnlyList<EditorPoint> RotateCorners(
        EditorRectangle rectangle,
        EditorPoint rotationCentre,
        double degrees)
        =>
        [
            Rotate(new EditorPoint(rectangle.Left, rectangle.Top), rotationCentre, degrees),
            Rotate(new EditorPoint(rectangle.Right, rectangle.Top), rotationCentre, degrees),
            Rotate(new EditorPoint(rectangle.Right, rectangle.Bottom), rotationCentre, degrees),
            Rotate(new EditorPoint(rectangle.Left, rectangle.Bottom), rotationCentre, degrees),
        ];

    private static double SourceAspectAfterCrop(
        CameraElementDefinition element,
        uint sourcePixelWidth,
        uint sourcePixelHeight)
    {
        var sourceAspect = sourcePixelWidth > 0 && sourcePixelHeight > 0
            ? (double)sourcePixelWidth / sourcePixelHeight
            : CanvasAspectRatio;
        return sourceAspect
               * (1 - element.CropLeft - element.CropRight)
               / (1 - element.CropTop - element.CropBottom);
    }
}

public enum EditorResizeCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
