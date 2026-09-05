namespace RoboCamHub.Runtime;

public enum CameraRuntimeState
{
    Stopped,
    Starting,
    Receiving,
    Failed,
    Stopping,
    WaitingToRetry,
}

public enum ViewRuntimeState
{
    Stopped,
    Running,
}

public enum ViewSourceRuntimeState
{
    Unbound,
    WaitingForFirstFrame,
    Live,
    FrozenLastGood,
    Reconnecting,
    MissingOrStale,
}

public enum OutputRuntimeState
{
    Stopped,
    Starting,
    Running,
    WaitingForViewFrame,
    Failed,
}

public readonly record struct CameraRuntimeStatus(
    CameraRuntimeState State,
    string LastResult,
    uint ActiveRtspSessionCount,
    uint ActiveDecoderCount,
    bool HasLatestFrame,
    uint LatestFrameWidth,
    uint LatestFrameHeight,
    ulong DecodedFrameCount,
    ulong LatestFrameSequence,
    ulong LatestFrameAgeMs,
    uint ReconnectAttemptCount,
    uint SuccessfulReconnectCount,
    uint NextRetryDelayMs,
    uint BoundViewSourceCount);

public readonly record struct ViewRuntimeStatus(
    ViewRuntimeState State,
    uint BoundSourceCount,
    uint LiveSourceCount,
    uint FrozenSourceCount,
    uint ReconnectingSourceCount,
    uint ConfiguredWidth,
    uint ConfiguredHeight,
    uint TargetFps,
    uint RenderFpsMilli,
    ulong LatestComposedFrameSequence,
    ulong LatestComposedFrameAgeMs,
    uint OutputConsumerCount);

public readonly record struct ViewSourceRuntimeStatus(
    uint SlotIndex,
    ViewSourceRuntimeState State,
    bool HasBinding,
    string? CameraId,
    bool SourceLive,
    bool FreezeCacheHasFrame);

public readonly record struct OutputRuntimeStatus(
    OutputRuntimeState State,
    string LastResult,
    string SenderName,
    uint ConfiguredWidth,
    uint ConfiguredHeight,
    uint TargetFps,
    uint SendFpsMilli,
    ulong SentFrameCount,
    ulong LatestSentSequence,
    ulong LatestSentFrameAgeMs,
    ulong DroppedOrSkippedFrameCount,
    uint AverageSendDurationUs,
    uint P95SendDurationUs,
    bool ReceiverCountKnown,
    uint ReceiverCount);

public readonly record struct ShowRuntimeDiagnostics(
    uint ConfiguredCameraCount,
    uint ActiveRtspSessionTotal,
    uint ActiveDecoderTotal,
    uint ViewCount,
    uint TotalBoundViewSourceCount);
