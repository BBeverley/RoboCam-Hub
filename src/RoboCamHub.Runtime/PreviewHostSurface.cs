namespace RoboCamHub.Runtime;

public enum PreviewHostPlatform
{
    WindowsHwnd,
    MacOSNsView,
}

public readonly record struct PreviewHostSurface(
    PreviewHostPlatform Platform,
    ulong NativeHandle,
    uint TargetFps = 30)
{
    public PreviewHostSurface Validate()
    {
        if (NativeHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(NativeHandle));
        }
        if (TargetFps is 0 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetFps));
        }
        return this;
    }
}
