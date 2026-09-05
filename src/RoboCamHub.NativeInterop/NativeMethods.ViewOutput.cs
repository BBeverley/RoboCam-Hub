using System.Runtime.InteropServices;

namespace RoboCamHub.NativeInterop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeViewStatusV1
{
    public uint struct_size;
    public uint struct_version;
    public uint bound_source_count;
    public uint sources_with_frame_count;
    public uint stale_or_missing_source_count;
    public uint reserved;
    public ulong last_observed_source_sequence;
    public NativeViewState render_state;
    public uint configured_width;
    public uint configured_height;
    public uint target_fps;
    public ulong render_frame_count;
    public ulong latest_composed_frame_sequence;
    public ulong latest_composed_frame_age_ms;
    public uint render_fps_milli;
    public uint sources_contributing_count;
    public uint output_consumer_count;
    public uint reserved_v2;
    public uint last_render_duration_us;
    public uint average_render_duration_us;
    public uint p95_render_duration_us;
    public uint stale_source_frame_count;
    public uint live_source_count;
    public uint waiting_for_first_frame_count;
    public uint frozen_source_count;
    public uint reconnecting_source_count;
    public ulong render_deadline_miss_count;
    public uint reserved_v3;
    public ulong last_render_deadline_miss_us;
    public ulong last_render_deadline_miss_sequence;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeViewSourceStatusV1
{
    public uint struct_size;
    public uint struct_version;
    public uint slot_index;
    public NativeViewSourceState source_state;
    public uint has_binding;
    public uint freeze_cache_has_frame;
    public uint source_live;
    public fixed byte camera_id_utf8[256];
    public ulong latest_observed_sequence;
    public ulong latest_source_frame_age_ms;
    public NativeCameraState camera_state;
    public uint reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeNdiSenderStatusV1
{
    public uint struct_size;
    public uint struct_version;
    public NativeNdiSenderState state;
    public uint configured_width;
    public uint configured_height;
    public uint target_fps;
    public NativeResult last_result;
    public ulong sent_frame_count;
    public ulong latest_sent_sequence;
    public ulong latest_sent_frame_age_ms;
    public uint send_fps_milli;
    public ulong dropped_or_skipped_frame_count;
    public uint last_send_duration_us;
    public uint average_send_duration_us;
    public uint p95_send_duration_us;
    public uint receiver_count;
    public fixed byte sender_name_utf8[256];
    public uint reserved;
    public ulong worker_tick_count;
    public ulong unique_sequence_observed_count;
    public ulong duplicate_sequence_tick_count;
    public uint receiver_count_known;
    public uint reserved_v2;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeViewPreviewConfigV1
{
    public uint struct_size;
    public uint struct_version;
    public ulong host_native_handle;
    public NativeViewPreviewPlatform platform;
    public uint target_fps;
    public fixed uint reserved[4];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeViewPreviewStatusV1
{
    public uint struct_size;
    public uint struct_version;
    public NativeViewPreviewState state;
    public NativeResult last_result;
    public uint attached;
    public uint configured_width;
    public uint configured_height;
    public uint target_fps;
    public uint presentation_fps_milli;
    public uint surface_recreate_count;
    public uint reserved;
    public ulong presented_frame_count;
    public ulong latest_presented_sequence;
    public ulong latest_presented_frame_age_ms;
    public ulong dropped_or_skipped_frame_count;
    public fixed byte view_id_utf8[256];
}

internal static partial class NativeMethods
{
    internal const uint ViewStatusVersion = 3;
    internal const uint ViewSourceStatusVersion = 1;
    internal const uint NdiSenderStatusVersion = 2;
    internal const uint ViewPreviewConfigVersion = 1;
    internal const uint ViewPreviewStatusVersion = 1;

    [LibraryImport(LibraryName, EntryPoint = "rch_view_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeResult ViewCreate(
        NativeEngineHandle engine,
        string viewIdUtf8,
        out NativeViewHandle view);

    [LibraryImport(LibraryName, EntryPoint = "rch_view_destroy")]
    internal static partial NativeResult ViewDestroy(nint view);

    [LibraryImport(LibraryName, EntryPoint = "rch_view_bind_camera_source", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeResult ViewBindCameraSource(
        NativeViewHandle view,
        uint slotIndex,
        string cameraIdUtf8);

    [LibraryImport(LibraryName, EntryPoint = "rch_view_unbind_source")]
    internal static partial NativeResult ViewUnbindSource(NativeViewHandle view, uint slotIndex);

    [LibraryImport(LibraryName, EntryPoint = "rch_view_get_status")]
    internal static partial NativeResult ViewGetStatus(
        NativeViewHandle view,
        ref NativeViewStatusV1 outStatus);

    [LibraryImport(LibraryName, EntryPoint = "rch_view_get_source_status")]
    internal static partial NativeResult ViewGetSourceStatus(
        NativeViewHandle view,
        uint slotIndex,
        ref NativeViewSourceStatusV1 outStatus);

    [LibraryImport(LibraryName, EntryPoint = "rch_ndi_sender_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeResult NdiSenderCreate(
        NativeViewHandle view,
        string senderNameUtf8,
        out NativeNdiSenderHandle sender);

    [LibraryImport(LibraryName, EntryPoint = "rch_ndi_sender_destroy")]
    internal static partial NativeResult NdiSenderDestroy(nint sender);

    [LibraryImport(LibraryName, EntryPoint = "rch_ndi_sender_start")]
    internal static partial NativeResult NdiSenderStart(NativeNdiSenderHandle sender);

    [LibraryImport(LibraryName, EntryPoint = "rch_ndi_sender_stop")]
    internal static partial NativeResult NdiSenderStop(NativeNdiSenderHandle sender);

    [LibraryImport(LibraryName, EntryPoint = "rch_ndi_sender_get_status")]
    internal static partial NativeResult NdiSenderGetStatus(
        NativeNdiSenderHandle sender,
        ref NativeNdiSenderStatusV1 outStatus);

    [LibraryImport(LibraryName, EntryPoint = "rch_view_preview_create")]
    internal static partial NativeResult ViewPreviewCreate(
        NativeViewHandle view,
        in NativeViewPreviewConfigV1 config,
        out NativeViewPreviewHandle preview);

    [LibraryImport(LibraryName, EntryPoint = "rch_view_preview_destroy")]
    internal static partial NativeResult ViewPreviewDestroy(nint preview);

    [LibraryImport(LibraryName, EntryPoint = "rch_view_preview_get_status")]
    internal static partial NativeResult ViewPreviewGetStatus(
        NativeViewPreviewHandle preview,
        ref NativeViewPreviewStatusV1 outStatus);
}
