namespace RoboCamHub.NativeInterop;

internal enum NativeResult : int
{
    Ok = 0,
    InvalidArgument = 1,
    InvalidHandle = 2,
    OutOfMemory = 3,
    InternalError = 4,
    InvalidState = 5,
    AlreadyStarted = 6,
    NotConfigured = 7,
    GStreamerError = 8,
    RtspFailure = 9,
    DecoderFailure = 10,
    ConnectionTimeout = 11,
}
