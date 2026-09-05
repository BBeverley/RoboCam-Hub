using Microsoft.Win32.SafeHandles;

namespace RoboCamHub.NativeInterop;

internal sealed class NativeViewPreviewHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public NativeViewPreviewHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
        => NativeMethods.ViewPreviewDestroy(handle) == NativeResult.Ok;
}
