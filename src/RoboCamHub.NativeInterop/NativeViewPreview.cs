using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RoboCamHub.NativeInterop;

public sealed class NativeViewPreview : IDisposable
{
    private NativeViewPreviewHandle? _handle;

    internal NativeViewPreview(NativeViewPreviewHandle handle)
    {
        _handle = handle;
    }

    public bool IsDisposed => Volatile.Read(ref _handle) is null;

    public unsafe NativeResult TryGetStatus(out NativeViewPreviewStatus status)
    {
        var nativeStatus = new NativeViewPreviewStatusV1
        {
            struct_size = (uint)Marshal.SizeOf<NativeViewPreviewStatusV1>(),
            struct_version = NativeMethods.ViewPreviewStatusVersion,
        };
        var result = NativeMethods.ViewPreviewGetStatus(GetHandleOrThrow(), ref nativeStatus);
        if (result == NativeResult.Ok)
        {
            byte* viewIdBytes = nativeStatus.view_id_utf8;
            status = new NativeViewPreviewStatus(
                nativeStatus.state,
                nativeStatus.last_result,
                nativeStatus.attached != 0,
                nativeStatus.configured_width,
                nativeStatus.configured_height,
                nativeStatus.target_fps,
                nativeStatus.presentation_fps_milli,
                nativeStatus.surface_recreate_count,
                nativeStatus.presented_frame_count,
                nativeStatus.latest_presented_sequence,
                nativeStatus.latest_presented_frame_age_ms,
                nativeStatus.dropped_or_skipped_frame_count,
                DecodeUtf8(viewIdBytes, 256));
        }
        else
        {
            status = default;
        }
        return result;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    private NativeViewPreviewHandle GetHandleOrThrow()
        => Volatile.Read(ref _handle) ?? throw new ObjectDisposedException(nameof(NativeViewPreview));

    private static unsafe string DecodeUtf8(byte* value, int capacity)
    {
        var length = 0;
        while (length < capacity && value[length] != 0)
        {
            length++;
        }
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(value, length));
    }
}
