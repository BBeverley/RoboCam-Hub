namespace RoboCamHub.NativeInterop;

public enum NativeViewSourceState : uint
{
    Unbound = 0,
    WaitingForFirstFrame = 1,
    Live = 2,
    FrozenLastGood = 3,
    Reconnecting = 4,
    MissingOrStale = 5,
}
