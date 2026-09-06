namespace RoboCamHub.Application;

public readonly record struct EditorPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public enum EditorResizeCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
