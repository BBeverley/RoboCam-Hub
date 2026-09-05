using RoboCamHub.Domain;
using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime;

public sealed class CameraRuntime
{
    private readonly ShowRuntime _owner;
    private readonly INativeRuntimeEngine _nativeEngine;
    private bool _disposed;

    internal CameraRuntime(
        ShowRuntime owner,
        INativeRuntimeEngine nativeEngine,
        CameraDefinition definition)
    {
        _owner = owner;
        _nativeEngine = nativeEngine;
        Definition = definition;
    }

    public CameraDefinition Definition { get; }

    public bool IsDisposed => _disposed;

    public void Start()
    {
        ThrowIfUnavailable();
        if (!Definition.Enabled)
        {
            throw new InvalidOperationException(
                $"Camera '{Definition.Id}' is disabled by its definition and cannot be started.");
        }

        RuntimeGuard.EnsureSuccess(
            $"Starting camera '{Definition.Id}'",
            _nativeEngine.StartCamera(Definition.Id));
    }

    public void Stop()
    {
        ThrowIfUnavailable();
        RuntimeGuard.EnsureSuccess(
            $"Stopping camera '{Definition.Id}'",
            _nativeEngine.StopCamera(Definition.Id));
    }

    public CameraRuntimeStatus GetStatus()
    {
        ThrowIfUnavailable();
        RuntimeGuard.EnsureSuccess(
            $"Querying camera '{Definition.Id}' status",
            _nativeEngine.TryGetCameraStatus(Definition.Id, out var status));
        return new CameraRuntimeStatus(
            MapState(status.State),
            status.LastResult.ToString(),
            status.ActiveRtspSessionCount,
            status.ActiveDecoderCount,
            status.HasLatestFrame,
            status.LatestFrameWidth,
            status.LatestFrameHeight,
            status.DecodedFrameCount,
            status.LatestFrameSequence,
            status.LatestFrameAgeMs,
            status.ReconnectAttemptCount,
            status.SuccessfulReconnectCount,
            status.NextRetryDelayMs,
            status.BoundViewSourceCount);
    }

    internal void DisposeOwned()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = _nativeEngine.StopCamera(Definition.Id);
        _ = _nativeEngine.RemoveCamera(Definition.Id);
    }

    private void ThrowIfUnavailable()
    {
        _owner.ThrowIfDisposed();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static CameraRuntimeState MapState(NativeCameraState state)
        => state switch
        {
            NativeCameraState.Stopped => CameraRuntimeState.Stopped,
            NativeCameraState.Starting => CameraRuntimeState.Starting,
            NativeCameraState.Receiving => CameraRuntimeState.Receiving,
            NativeCameraState.Failed => CameraRuntimeState.Failed,
            NativeCameraState.Stopping => CameraRuntimeState.Stopping,
            NativeCameraState.WaitingToRetry => CameraRuntimeState.WaitingToRetry,
            _ => throw new InvalidOperationException($"Unknown native camera state {(uint)state}."),
        };
}
