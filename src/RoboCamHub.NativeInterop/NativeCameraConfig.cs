namespace RoboCamHub.NativeInterop;

public readonly record struct NativeCameraConfig(
    string CameraId,
    string RtspUrl,
    uint ConnectTimeoutMs = 10_000,
    uint Reserved = 0);
