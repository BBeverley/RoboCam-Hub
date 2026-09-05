using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RoboCamHub.NativeInterop;

public sealed class NativeNdiSender : IDisposable
{
    private NativeNdiSenderHandle? _handle;

    internal NativeNdiSender(NativeNdiSenderHandle handle)
    {
        _handle = handle;
    }

    public bool IsDisposed => Volatile.Read(ref _handle) is null;

    public NativeResult Start()
        => NativeMethods.NdiSenderStart(GetHandleOrThrow());

    public NativeResult Stop()
        => NativeMethods.NdiSenderStop(GetHandleOrThrow());

    public unsafe NativeResult TryGetStatus(out NativeNdiSenderStatus status)
    {
        var nativeStatus = new NativeNdiSenderStatusV1
        {
            struct_size = (uint)Marshal.SizeOf<NativeNdiSenderStatusV1>(),
            struct_version = NativeMethods.NdiSenderStatusVersion,
        };

        var result = NativeMethods.NdiSenderGetStatus(GetHandleOrThrow(), ref nativeStatus);
        if (result == NativeResult.Ok)
        {
            byte* value = nativeStatus.sender_name_utf8;
            var senderName = DecodeUtf8(value, 256);

            status = new NativeNdiSenderStatus(
                nativeStatus.state,
                nativeStatus.configured_width,
                nativeStatus.configured_height,
                nativeStatus.target_fps,
                nativeStatus.last_result,
                nativeStatus.sent_frame_count,
                nativeStatus.latest_sent_sequence,
                nativeStatus.latest_sent_frame_age_ms,
                nativeStatus.send_fps_milli,
                nativeStatus.dropped_or_skipped_frame_count,
                nativeStatus.last_send_duration_us,
                nativeStatus.average_send_duration_us,
                nativeStatus.p95_send_duration_us,
                nativeStatus.receiver_count,
                senderName,
                nativeStatus.worker_tick_count,
                nativeStatus.unique_sequence_observed_count,
                nativeStatus.duplicate_sequence_tick_count,
                nativeStatus.receiver_count_known != 0,
                nativeStatus.reserved_v2 != 0);
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

    private NativeNdiSenderHandle GetHandleOrThrow()
        => Volatile.Read(ref _handle) ?? throw new ObjectDisposedException(nameof(NativeNdiSender));

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
