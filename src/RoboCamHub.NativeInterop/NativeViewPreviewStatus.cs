namespace RoboCamHub.NativeInterop;

public readonly record struct NativeViewPreviewStatus(
    NativeViewPreviewState State,
    NativeResult LastResult,
    bool Attached,
    uint ConfiguredWidth,
    uint ConfiguredHeight,
    uint TargetFps,
    uint PresentationFpsMilli,
    uint SurfaceRecreateCount,
    ulong PresentedFrameCount,
    ulong LatestPresentedSequence,
    ulong LatestPresentedFrameAgeMs,
    ulong DroppedOrSkippedFrameCount,
    string ViewId);
