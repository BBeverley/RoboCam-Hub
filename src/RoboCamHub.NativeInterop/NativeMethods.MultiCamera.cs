using System.Runtime.InteropServices;

namespace RoboCamHub.NativeInterop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCameraConfigV1
{
    public uint struct_size;
    public uint struct_version;
    public nint camera_id_utf8;
    public nint rtsp_url_utf8;
    public uint connect_timeout_ms;
    public uint reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCameraStatusV1
{
    public uint struct_size;
    public uint struct_version;
    public NativeCameraState state;
    public NativeResult last_result;
    public uint active_rtsp_session_count;
    public uint active_decoder_count;
    public uint has_latest_frame;
    public uint latest_frame_width;
    public uint latest_frame_height;
    public uint reserved;
    public ulong decoded_frame_count;
    public ulong latest_frame_sequence;
    public ulong latest_frame_timestamp_ns;
    public ulong latest_frame_age_ms;
    public uint reconnect_attempt_count;
    public uint successful_reconnect_count;
    public uint next_retry_delay_ms;
    public uint reserved_v2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEngineDiagnosticsV1
{
    public uint struct_size;
    public uint struct_version;
    public uint configured_camera_count;
    public uint active_rtsp_session_total;
    public uint active_decoder_total;
    public uint cameras_starting_count;
    public uint cameras_receiving_count;
    public uint cameras_waiting_to_retry_count;
    public uint cameras_failed_count;
    public uint cameras_stopped_count;
    public uint reserved;
    public ulong successful_reconnect_total;
}

internal static partial class NativeMethods
{
    internal const uint CameraConfigVersion = 1;
    internal const uint CameraStatusVersion = 2;
    internal const uint EngineDiagnosticsVersion = 1;

    [LibraryImport(LibraryName, EntryPoint = "rch_camera_add", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeResult CameraAdd(NativeEngineHandle engine, in NativeCameraConfigV1 config);

    [LibraryImport(LibraryName, EntryPoint = "rch_camera_remove", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeResult CameraRemove(NativeEngineHandle engine, string cameraIdUtf8);

    [LibraryImport(LibraryName, EntryPoint = "rch_camera_start_by_id", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeResult CameraStartById(NativeEngineHandle engine, string cameraIdUtf8);

    [LibraryImport(LibraryName, EntryPoint = "rch_camera_stop_by_id", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeResult CameraStopById(NativeEngineHandle engine, string cameraIdUtf8);

    [LibraryImport(LibraryName, EntryPoint = "rch_camera_get_status_by_id", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeResult CameraGetStatusById(
        NativeEngineHandle engine,
        string cameraIdUtf8,
        ref NativeCameraStatusV1 outStatus);

    [LibraryImport(LibraryName, EntryPoint = "rch_camera_enumerate_ids")]
    internal static partial NativeResult CameraEnumerateIds(
        NativeEngineHandle engine,
        byte[]? outIdsUtf8Buffer,
        uint outIdsUtf8BufferSize,
        out uint outRequiredBufferSize,
        out uint outCameraCount);

    [LibraryImport(LibraryName, EntryPoint = "rch_engine_get_diagnostics")]
    internal static partial NativeResult EngineGetDiagnostics(
        NativeEngineHandle engine,
        ref NativeEngineDiagnosticsV1 outDiagnostics);
}
