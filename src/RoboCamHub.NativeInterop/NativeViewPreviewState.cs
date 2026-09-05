namespace RoboCamHub.NativeInterop;

public enum NativeViewPreviewState : uint
{
    Starting = 0,
    Live = 1,
    WaitingForView = 2,
    Failed = 3,
}
