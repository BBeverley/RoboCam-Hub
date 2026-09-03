namespace RoboCamHub.NativeInterop;

internal enum NativeResult : int
{
    Ok = 0,
    InvalidArgument = 1,
    InvalidHandle = 2,
    OutOfMemory = 3,
    InternalError = 4,
}
