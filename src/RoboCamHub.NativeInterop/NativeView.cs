using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RoboCamHub.NativeInterop;

public sealed class NativeView : IDisposable
{
    public const uint SourceSlotCount = 4;

    private NativeViewHandle? _handle;

    internal NativeView(NativeViewHandle handle)
    {
        _handle = handle;
    }

    public bool IsDisposed => Volatile.Read(ref _handle) is null;

    public NativeResult BindCameraSource(uint slotIndex, string cameraId)
        => NativeMethods.ViewBindCameraSource(
            GetHandleOrThrow(),
            ValidateSlot(slotIndex),
            ValidateText(cameraId, nameof(cameraId), "Camera ID"));

    public NativeResult UnbindSource(uint slotIndex)
        => NativeMethods.ViewUnbindSource(GetHandleOrThrow(), ValidateSlot(slotIndex));

    public NativeResult TryGetStatus(out NativeViewStatus status)
    {
        var nativeStatus = new NativeViewStatusV1
        {
            struct_size = (uint)Marshal.SizeOf<NativeViewStatusV1>(),
            struct_version = NativeMethods.ViewStatusVersion,
        };

        var result = NativeMethods.ViewGetStatus(GetHandleOrThrow(), ref nativeStatus);
        status = result == NativeResult.Ok
            ? new NativeViewStatus(
                nativeStatus.bound_source_count,
                nativeStatus.sources_with_frame_count,
                nativeStatus.stale_or_missing_source_count,
                nativeStatus.last_observed_source_sequence,
                nativeStatus.render_state,
                nativeStatus.configured_width,
                nativeStatus.configured_height,
                nativeStatus.target_fps,
                nativeStatus.render_frame_count,
                nativeStatus.latest_composed_frame_sequence,
                nativeStatus.latest_composed_frame_age_ms,
                nativeStatus.render_fps_milli,
                nativeStatus.sources_contributing_count,
                nativeStatus.output_consumer_count,
                nativeStatus.last_render_duration_us,
                nativeStatus.average_render_duration_us,
                nativeStatus.p95_render_duration_us,
                nativeStatus.stale_source_frame_count,
                nativeStatus.live_source_count,
                nativeStatus.waiting_for_first_frame_count,
                nativeStatus.frozen_source_count,
                nativeStatus.reconnecting_source_count,
                nativeStatus.render_deadline_miss_count,
                nativeStatus.last_render_deadline_miss_us,
                nativeStatus.last_render_deadline_miss_sequence)
            : default;
        return result;
    }

    public unsafe NativeResult TryGetSourceStatus(uint slotIndex, out NativeViewSourceStatus status)
    {
        var nativeStatus = new NativeViewSourceStatusV1
        {
            struct_size = (uint)Marshal.SizeOf<NativeViewSourceStatusV1>(),
            struct_version = NativeMethods.ViewSourceStatusVersion,
        };

        var result = NativeMethods.ViewGetSourceStatus(
            GetHandleOrThrow(),
            ValidateSlot(slotIndex),
            ref nativeStatus);
        if (result == NativeResult.Ok)
        {
            byte* value = nativeStatus.camera_id_utf8;
            var cameraId = nativeStatus.has_binding == 0 ? null : DecodeUtf8(value, 256);

            status = new NativeViewSourceStatus(
                nativeStatus.slot_index,
                nativeStatus.source_state,
                nativeStatus.has_binding != 0,
                nativeStatus.freeze_cache_has_frame != 0,
                nativeStatus.source_live != 0,
                cameraId,
                nativeStatus.latest_observed_sequence,
                nativeStatus.latest_source_frame_age_ms,
                nativeStatus.camera_state);
        }
        else
        {
            status = default;
        }

        return result;
    }

    public NativeResult TryCreateNdiSender(string senderName, out NativeNdiSender? sender)
    {
        var result = NativeMethods.NdiSenderCreate(
            GetHandleOrThrow(),
            ValidateText(senderName, nameof(senderName), "NDI sender name"),
            out var handle);
        if (result != NativeResult.Ok || handle is null || handle.IsInvalid)
        {
            handle?.Dispose();
            sender = null;
            return result == NativeResult.Ok ? NativeResult.InternalError : result;
        }

        sender = new NativeNdiSender(handle);
        return NativeResult.Ok;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    private NativeViewHandle GetHandleOrThrow()
        => Volatile.Read(ref _handle) ?? throw new ObjectDisposedException(nameof(NativeView));

    private static uint ValidateSlot(uint slotIndex)
    {
        if (slotIndex >= SourceSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        return slotIndex;
    }

    private static string ValidateText(string value, string parameterName, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{description} must not be empty.", parameterName);
        }

        return value;
    }

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
