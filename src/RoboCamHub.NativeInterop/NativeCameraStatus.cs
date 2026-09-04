namespace RoboCamHub.NativeInterop;

public readonly record struct NativeCameraStatus(
    NativeCameraState State,
    NativeResult LastResult,
    uint ActiveRtspSessionCount,
    uint ActiveDecoderCount,
    bool HasLatestFrame,
    uint LatestFrameWidth,
    uint LatestFrameHeight,
    ulong DecodedFrameCount,
    ulong LatestFrameSequence,
    ulong LatestFrameTimestampNs,
    ulong LatestFrameAgeMs,
    uint ReconnectAttemptCount,
    uint SuccessfulReconnectCount,
    uint NextRetryDelayMs);
