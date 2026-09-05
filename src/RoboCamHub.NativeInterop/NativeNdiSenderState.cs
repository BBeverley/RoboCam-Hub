namespace RoboCamHub.NativeInterop;

public enum NativeNdiSenderState : uint
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    WaitingForViewFrame = 3,
    Failed = 4,
}
