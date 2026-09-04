namespace RoboCamHub.NativeInterop;

public enum NativeCameraState : uint
{
    Stopped = 0,
    Starting = 1,
    Receiving = 2,
    Failed = 3,
    Stopping = 4,
    WaitingToRetry = 5,
}
