using System.Threading;
using System.Runtime.InteropServices;
using System.Text;

namespace RoboCamHub.NativeInterop;

public sealed class NativeEngine : IDisposable
{
    private NativeEngineHandle? _handle;

    private NativeEngine(NativeEngineHandle handle)
    {
        _handle = handle;
    }

    public bool IsDisposed => Volatile.Read(ref _handle) is null;

    public static NativeEngine Create() => Create(new NativeAbiVersionQuery());

    internal static NativeEngine Create(INativeAbiVersionQuery versionQuery)
    {
        ArgumentNullException.ThrowIfNull(versionQuery);

        var actualVersion = versionQuery.GetVersion();
        if (actualVersion != NativeAbiVersion.Supported)
        {
            throw new NativeAbiMismatchException(NativeAbiVersion.Supported, actualVersion);
        }

        var result = NativeMethods.EngineCreate(out var handle);
        if (result != NativeResult.Ok)
        {
            handle?.Dispose();
            throw new InvalidOperationException(
                $"Native engine creation failed with result {result} ({(int)result}).");
        }

        if (handle is null || handle.IsInvalid)
        {
            handle?.Dispose();
            throw new InvalidOperationException("Native engine creation returned an invalid handle.");
        }

        return new NativeEngine(handle);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    public NativeResult AddOrUpdateCamera(in NativeCameraConfig config)
    {
        var handle = GetHandleOrThrow();
        ValidateCameraConfig(config);

        using var idUtf8 = Utf8HeapString.Create(config.CameraId);
        using var urlUtf8 = Utf8HeapString.Create(config.RtspUrl);

        var nativeConfig = new NativeCameraConfigV1
        {
            struct_size = (uint)Marshal.SizeOf<NativeCameraConfigV1>(),
            struct_version = NativeMethods.CameraConfigVersion,
            camera_id_utf8 = idUtf8.Pointer,
            rtsp_url_utf8 = urlUtf8.Pointer,
            connect_timeout_ms = config.ConnectTimeoutMs,
            reserved = config.Reserved,
        };

        return NativeMethods.CameraAdd(handle, in nativeConfig);
    }

    public NativeResult RemoveCamera(string cameraId)
        => NativeMethods.CameraRemove(GetHandleOrThrow(), ValidateCameraId(cameraId));

    public NativeResult StartCamera(string cameraId)
        => NativeMethods.CameraStartById(GetHandleOrThrow(), ValidateCameraId(cameraId));

    public NativeResult StopCamera(string cameraId)
        => NativeMethods.CameraStopById(GetHandleOrThrow(), ValidateCameraId(cameraId));

    public NativeResult TryGetCameraStatus(string cameraId, out NativeCameraStatus status)
    {
        var nativeStatus = new NativeCameraStatusV1
        {
            struct_size = (uint)Marshal.SizeOf<NativeCameraStatusV1>(),
            struct_version = NativeMethods.CameraStatusVersion,
        };

        var result = NativeMethods.CameraGetStatusById(
            GetHandleOrThrow(),
            ValidateCameraId(cameraId),
            ref nativeStatus);

        status = result == NativeResult.Ok
            ? new NativeCameraStatus(
                nativeStatus.state,
                nativeStatus.last_result,
                nativeStatus.active_rtsp_session_count,
                nativeStatus.active_decoder_count,
                nativeStatus.has_latest_frame != 0,
                nativeStatus.latest_frame_width,
                nativeStatus.latest_frame_height,
                nativeStatus.decoded_frame_count,
                nativeStatus.latest_frame_sequence,
                nativeStatus.latest_frame_timestamp_ns,
                nativeStatus.latest_frame_age_ms,
                nativeStatus.reconnect_attempt_count,
                nativeStatus.successful_reconnect_count,
                nativeStatus.next_retry_delay_ms)
            : default;

        return result;
    }

    public NativeResult TryEnumerateCameraIds(out IReadOnlyList<string> cameraIds)
    {
        var handle = GetHandleOrThrow();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var countResult = NativeMethods.CameraEnumerateIds(handle, null, 0, out var requiredBytes, out var cameraCount);
            if (countResult != NativeResult.Ok)
            {
                cameraIds = Array.Empty<string>();
                return countResult;
            }

            if (requiredBytes == 0 || cameraCount == 0)
            {
                cameraIds = Array.Empty<string>();
                return NativeResult.Ok;
            }

            var buffer = new byte[requiredBytes];
            var listResult = NativeMethods.CameraEnumerateIds(handle, buffer, requiredBytes, out requiredBytes, out cameraCount);
            if (listResult == NativeResult.BufferTooSmall)
            {
                continue;
            }

            if (listResult != NativeResult.Ok)
            {
                cameraIds = Array.Empty<string>();
                return listResult;
            }

            if (!TryDecodeEnumeratedIds(buffer, cameraCount, out var parsed))
            {
                cameraIds = Array.Empty<string>();
                return NativeResult.InternalError;
            }

            cameraIds = parsed;
            return NativeResult.Ok;
        }

        cameraIds = Array.Empty<string>();
        return NativeResult.BufferTooSmall;
    }

    public NativeResult TryGetEngineDiagnostics(out NativeEngineDiagnostics diagnostics)
    {
        var nativeDiagnostics = new NativeEngineDiagnosticsV1
        {
            struct_size = (uint)Marshal.SizeOf<NativeEngineDiagnosticsV1>(),
            struct_version = NativeMethods.EngineDiagnosticsVersion,
        };

        var result = NativeMethods.EngineGetDiagnostics(GetHandleOrThrow(), ref nativeDiagnostics);
        diagnostics = result == NativeResult.Ok
            ? new NativeEngineDiagnostics(
                nativeDiagnostics.configured_camera_count,
                nativeDiagnostics.active_rtsp_session_total,
                nativeDiagnostics.active_decoder_total,
                nativeDiagnostics.cameras_starting_count,
                nativeDiagnostics.cameras_receiving_count,
                nativeDiagnostics.cameras_waiting_to_retry_count,
                nativeDiagnostics.cameras_failed_count,
                nativeDiagnostics.cameras_stopped_count,
                nativeDiagnostics.successful_reconnect_total)
            : default;

        return result;
    }

    private NativeEngineHandle GetHandleOrThrow()
        => Volatile.Read(ref _handle) ?? throw new ObjectDisposedException(nameof(NativeEngine));

    private static string ValidateCameraId(string cameraId)
    {
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            throw new ArgumentException("Camera ID must be a non-empty UTF-8 string.", nameof(cameraId));
        }

        return cameraId;
    }

    private static void ValidateCameraConfig(in NativeCameraConfig config)
    {
        _ = ValidateCameraId(config.CameraId);
        if (string.IsNullOrWhiteSpace(config.RtspUrl))
        {
            throw new ArgumentException("RTSP URL must be a non-empty UTF-8 string.", nameof(config));
        }
    }

    private static bool TryDecodeEnumeratedIds(byte[] buffer, uint expectedCount, out IReadOnlyList<string> cameraIds)
    {
        var ids = new List<string>((int)expectedCount);
        var offset = 0;
        for (var i = 0; i < expectedCount; i++)
        {
            if (offset >= buffer.Length)
            {
                cameraIds = Array.Empty<string>();
                return false;
            }

            var terminator = Array.IndexOf(buffer, (byte)0, offset);
            if (terminator < 0)
            {
                cameraIds = Array.Empty<string>();
                return false;
            }

            var length = terminator - offset;
            ids.Add(Encoding.UTF8.GetString(buffer, offset, length));
            offset = terminator + 1;
        }

        cameraIds = ids;
        return true;
    }

    private readonly struct Utf8HeapString : IDisposable
    {
        public nint Pointer { get; }

        private Utf8HeapString(nint pointer)
        {
            Pointer = pointer;
        }

        public static Utf8HeapString Create(string value)
            => new(Marshal.StringToCoTaskMemUTF8(value));

        public void Dispose()
        {
            if (Pointer != nint.Zero)
            {
                Marshal.FreeCoTaskMem(Pointer);
            }
        }
    }
}
