using Microsoft.Win32.SafeHandles;

namespace RoboCamHub.NativeInterop;

internal sealed class NativeEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public NativeEngineHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
        => NativeMethods.EngineDestroy(handle) == NativeResult.Ok;
}
