using Microsoft.Win32.SafeHandles;

namespace RoboCamHub.NativeInterop;

internal sealed class NativeViewHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public NativeViewHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
        => NativeMethods.ViewDestroy(handle) == NativeResult.Ok;
}
