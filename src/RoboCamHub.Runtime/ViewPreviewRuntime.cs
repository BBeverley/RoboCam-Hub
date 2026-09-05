using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime;

public sealed class ViewPreviewRuntime : IDisposable
{
    private readonly ShowRuntime _owner;
    private readonly INativeRuntimePreview _nativePreview;
    private bool _disposed;

    internal ViewPreviewRuntime(
        ShowRuntime owner,
        ViewRuntime view,
        INativeRuntimePreview nativePreview)
    {
        _owner = owner;
        View = view;
        _nativePreview = nativePreview;
    }

    public ViewRuntime View { get; }

    public bool IsDisposed => _disposed;

    public ViewPreviewRuntimeStatus GetStatus()
    {
        ThrowIfUnavailable();
        RuntimeGuard.EnsureSuccess(
            $"Querying preview for View '{View.Definition.Id}'",
            _nativePreview.TryGetStatus(out var status));
        return new ViewPreviewRuntimeStatus(
            MapState(status.State),
            status.LastResult.ToString(),
            status.Attached,
            status.ViewId,
            status.ConfiguredWidth,
            status.ConfiguredHeight,
            status.TargetFps,
            status.PresentationFpsMilli,
            status.PresentedFrameCount,
            status.LatestPresentedSequence,
            status.LatestPresentedFrameAgeMs,
            status.DroppedOrSkippedFrameCount,
            status.SurfaceRecreateCount);
    }

    public void Dispose()
    {
        _owner.DisposePreview(this);
        GC.SuppressFinalize(this);
    }

    internal void DisposeOwned()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _nativePreview.Dispose();
    }

    private void ThrowIfUnavailable()
    {
        _owner.ThrowIfDisposed();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static ViewPreviewRuntimeState MapState(NativeViewPreviewState state)
        => state switch
        {
            NativeViewPreviewState.Starting => ViewPreviewRuntimeState.Starting,
            NativeViewPreviewState.Live => ViewPreviewRuntimeState.Live,
            NativeViewPreviewState.WaitingForView => ViewPreviewRuntimeState.WaitingForView,
            NativeViewPreviewState.Failed => ViewPreviewRuntimeState.Failed,
            _ => throw new InvalidOperationException($"Unknown native preview state {(uint)state}."),
        };
}
