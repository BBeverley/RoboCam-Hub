using Microsoft.Win32.SafeHandles;

namespace RoboCamHub.NativeInterop;

internal sealed class NativeNdiSenderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public NativeNdiSenderHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
        => NativeMethods.NdiSenderDestroy(handle) == NativeResult.Ok;
}
