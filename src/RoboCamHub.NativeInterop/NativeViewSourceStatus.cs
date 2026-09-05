namespace RoboCamHub.NativeInterop;

public readonly record struct NativeViewSourceStatus(
    uint SlotIndex,
    NativeViewSourceState State,
    bool HasBinding,
    bool FreezeCacheHasFrame,
    bool SourceLive,
    string? CameraId,
    ulong LatestObservedSequence,
    ulong LatestSourceFrameAgeMs,
    NativeCameraState CameraState);
