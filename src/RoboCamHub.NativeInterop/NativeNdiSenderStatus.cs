namespace RoboCamHub.NativeInterop;

public readonly record struct NativeNdiSenderStatus(
    NativeNdiSenderState State,
    uint ConfiguredWidth,
    uint ConfiguredHeight,
    uint TargetFps,
    NativeResult LastResult,
    ulong SentFrameCount,
    ulong LatestSentSequence,
    ulong LatestSentFrameAgeMs,
    uint SendFpsMilli,
    ulong DroppedOrSkippedFrameCount,
    uint LastSendDurationUs,
    uint AverageSendDurationUs,
    uint P95SendDurationUs,
    uint ReceiverCount,
    string SenderName,
    ulong WorkerTickCount,
    ulong UniqueSequenceObservedCount,
    ulong DuplicateSequenceTickCount,
    bool ReceiverCountKnown,
    bool IsOfficialSdkBackend);
