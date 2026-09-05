namespace RoboCamHub.NativeInterop;

public readonly record struct NativeEngineDiagnostics(
    uint ConfiguredCameraCount,
    uint ActiveRtspSessionTotal,
    uint ActiveDecoderTotal,
    uint CamerasStartingCount,
    uint CamerasReceivingCount,
    uint CamerasWaitingToRetryCount,
    uint CamerasFailedCount,
    uint CamerasStoppedCount,
    ulong SuccessfulReconnectTotal,
    uint ViewCount,
    uint DirectFrameConsumerCount,
    uint TotalBoundViewSourceCount);
