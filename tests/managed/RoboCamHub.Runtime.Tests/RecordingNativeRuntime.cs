using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime.Tests;

internal sealed class RecordingNativeRuntimeFactory : INativeRuntimeFactory
{
    public List<string> Events { get; } = [];

    public RecordingNativeRuntimeEngine? Engine { get; private set; }

    public INativeRuntimeEngine CreateEngine()
    {
        Events.Add("engine:create");
        Engine = new RecordingNativeRuntimeEngine(Events);
        return Engine;
    }
}

internal sealed class RecordingNativeRuntimeEngine(List<string> events) : INativeRuntimeEngine
{
    private readonly HashSet<string> _cameraIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _startedCameraIds = new(StringComparer.Ordinal);
    private readonly List<RecordingNativeRuntimeView> _views = [];

    public int AddCameraCallCount { get; private set; }

    public NativeResult AddOrUpdateCamera(in NativeCameraConfig config)
    {
        AddCameraCallCount++;
        _cameraIds.Add(config.CameraId);
        events.Add($"camera:add:{config.CameraId}:{config.RtspUrl}:{config.ConnectTimeoutMs}");
        return NativeResult.Ok;
    }

    public NativeResult RemoveCamera(string cameraId)
    {
        _cameraIds.Remove(cameraId);
        _startedCameraIds.Remove(cameraId);
        events.Add($"camera:remove:{cameraId}");
        return NativeResult.Ok;
    }

    public NativeResult StartCamera(string cameraId)
    {
        events.Add($"camera:start:{cameraId}");
        if (!_cameraIds.Contains(cameraId))
        {
            return NativeResult.NotConfigured;
        }

        _startedCameraIds.Add(cameraId);
        return NativeResult.Ok;
    }

    public NativeResult StopCamera(string cameraId)
    {
        events.Add($"camera:stop:{cameraId}");
        if (!_cameraIds.Contains(cameraId))
        {
            return NativeResult.NotConfigured;
        }

        _startedCameraIds.Remove(cameraId);
        return NativeResult.Ok;
    }

    public NativeResult TryGetCameraStatus(string cameraId, out NativeCameraStatus status)
    {
        events.Add($"camera:status:{cameraId}");
        status = default(NativeCameraStatus) with
        {
            State = _startedCameraIds.Contains(cameraId)
                ? NativeCameraState.Receiving
                : NativeCameraState.Stopped,
            LastResult = NativeResult.Ok,
            ActiveRtspSessionCount = _startedCameraIds.Contains(cameraId) ? 1U : 0U,
            ActiveDecoderCount = _startedCameraIds.Contains(cameraId) ? 1U : 0U,
            HasLatestFrame = _startedCameraIds.Contains(cameraId),
            LatestFrameWidth = 1280,
            LatestFrameHeight = 720,
            DecodedFrameCount = 42,
            LatestFrameSequence = 41,
            LatestFrameAgeMs = 7,
            BoundViewSourceCount = 1,
        };
        return _cameraIds.Contains(cameraId) ? NativeResult.Ok : NativeResult.NotConfigured;
    }

    public NativeResult TryGetDiagnostics(out NativeEngineDiagnostics diagnostics)
    {
        events.Add("engine:diagnostics");
        diagnostics = default(NativeEngineDiagnostics) with
        {
            ConfiguredCameraCount = (uint)_cameraIds.Count,
            ActiveRtspSessionTotal = (uint)_startedCameraIds.Count,
            ActiveDecoderTotal = (uint)_startedCameraIds.Count,
            ViewCount = (uint)_views.Count(view => !view.IsDisposed),
            TotalBoundViewSourceCount = (uint)_views.Sum(view => view.BindingCount),
        };
        return NativeResult.Ok;
    }

    public NativeResult TryCreateView(string viewId, out INativeRuntimeView? view)
    {
        events.Add($"view:create:{viewId}");
        var created = new RecordingNativeRuntimeView(events, viewId);
        _views.Add(created);
        view = created;
        return NativeResult.Ok;
    }

    public void Dispose()
    {
        events.Add("engine:dispose");
    }
}

internal sealed class RecordingNativeRuntimeView(List<string> events, string viewId) : INativeRuntimeView
{
    private readonly Dictionary<uint, string> _bindings = [];

    public bool IsDisposed { get; private set; }

    public int BindingCount => IsDisposed ? 0 : _bindings.Count;

    public NativeResult BindCameraSource(uint slotIndex, string cameraId)
    {
        _bindings[slotIndex] = cameraId;
        events.Add($"view:bind:{viewId}:{slotIndex}:{cameraId}");
        return NativeResult.Ok;
    }

    public NativeResult UnbindSource(uint slotIndex)
    {
        _bindings.Remove(slotIndex);
        events.Add($"view:unbind:{viewId}:{slotIndex}");
        return NativeResult.Ok;
    }

    public NativeResult TryGetStatus(out NativeViewStatus status)
    {
        events.Add($"view:status:{viewId}");
        status = default(NativeViewStatus) with
        {
            State = NativeViewState.Running,
            BoundSourceCount = (uint)_bindings.Count,
            LiveSourceCount = (uint)_bindings.Count,
            ConfiguredWidth = 1920,
            ConfiguredHeight = 1080,
            TargetFps = 60,
            RenderFpsMilli = 60_000,
            LatestComposedFrameSequence = 100,
            LatestComposedFrameAgeMs = 5,
        };
        return NativeResult.Ok;
    }

    public NativeResult TryGetSourceStatus(uint slotIndex, out NativeViewSourceStatus status)
    {
        events.Add($"view:source-status:{viewId}:{slotIndex}");
        var hasBinding = _bindings.TryGetValue(slotIndex, out var cameraId);
        status = default(NativeViewSourceStatus) with
        {
            SlotIndex = slotIndex,
            State = hasBinding ? NativeViewSourceState.Live : NativeViewSourceState.Unbound,
            HasBinding = hasBinding,
            CameraId = cameraId,
            SourceLive = hasBinding,
        };
        return NativeResult.Ok;
    }

    public NativeResult TryCreateSender(string senderName, out INativeRuntimeSender? sender)
    {
        events.Add($"sender:create:{viewId}:{senderName}");
        sender = new RecordingNativeRuntimeSender(events, senderName);
        return NativeResult.Ok;
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        events.Add($"view:dispose:{viewId}");
    }
}

internal sealed class RecordingNativeRuntimeSender(List<string> events, string senderName) : INativeRuntimeSender
{
    private NativeNdiSenderState _state = NativeNdiSenderState.Stopped;
    private bool _disposed;

    public NativeResult Start()
    {
        events.Add($"sender:start:{senderName}");
        _state = NativeNdiSenderState.Running;
        return NativeResult.Ok;
    }

    public NativeResult Stop()
    {
        events.Add($"sender:stop:{senderName}");
        _state = NativeNdiSenderState.Stopped;
        return NativeResult.Ok;
    }

    public NativeResult TryGetStatus(out NativeNdiSenderStatus status)
    {
        events.Add($"sender:status:{senderName}");
        status = default(NativeNdiSenderStatus) with
        {
            State = _state,
            LastResult = NativeResult.Ok,
            SenderName = senderName,
            ConfiguredWidth = 1920,
            ConfiguredHeight = 1080,
            TargetFps = 60,
        };
        return NativeResult.Ok;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        events.Add($"sender:dispose:{senderName}");
    }
}
