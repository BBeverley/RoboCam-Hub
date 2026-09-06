using RoboCamHub.Domain;
using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime;

public sealed class ViewRuntime : IDisposable
{
    private readonly ShowRuntime _owner;
    private readonly INativeRuntimeView _nativeView;
    private bool _disposed;

    internal ViewRuntime(
        ShowRuntime owner,
        INativeRuntimeView nativeView,
        ViewDefinition definition)
    {
        _owner = owner;
        _nativeView = nativeView;
        Definition = definition;
    }

    public ViewDefinition Definition { get; private set; }

    public bool IsDisposed => _disposed;

    internal INativeRuntimeView NativeView => _nativeView;

    public void ApplyScene(IReadOnlyList<ViewSceneElementDefinition> elements)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(elements);

        var updatedDefinition = new ViewDefinition(Definition.Id, Definition.Name, elements);

        var nativeElements = new List<NativeCameraElementConfig>(updatedDefinition.SceneElements.Count);
        foreach (var element in updatedDefinition.SceneElements)
        {
            ArgumentNullException.ThrowIfNull(element);
            if (element is not CameraElementDefinition cameraElement)
            {
                throw new NotSupportedException(
                    $"Scene element type '{element.GetType().Name}' is not supported by Gate 6A.");
            }

            _ = _owner.GetCamera(cameraElement.CameraId);
            nativeElements.Add(ToNative(cameraElement));
        }

        RuntimeGuard.EnsureSuccess(
            $"Applying View '{Definition.Id}' scene",
            _nativeView.ApplyCameraScene(nativeElements));
        Definition = updatedDefinition;
    }

    public void BindCameraSource(uint slotIndex, string cameraId)
    {
        ThrowIfUnavailable();
        var camera = _owner.GetCamera(cameraId);
        RuntimeGuard.EnsureSuccess(
            $"Binding View '{Definition.Id}' slot {slotIndex} to camera '{camera.Definition.Id}'",
            _nativeView.BindCameraSource(slotIndex, camera.Definition.Id));
    }

    public void UnbindSource(uint slotIndex)
    {
        ThrowIfUnavailable();
        RuntimeGuard.EnsureSuccess(
            $"Unbinding View '{Definition.Id}' slot {slotIndex}",
            _nativeView.UnbindSource(slotIndex));
    }

    public ViewRuntimeStatus GetStatus()
    {
        ThrowIfUnavailable();
        RuntimeGuard.EnsureSuccess(
            $"Querying View '{Definition.Id}' status",
            _nativeView.TryGetStatus(out var status));
        return new ViewRuntimeStatus(
            MapState(status.State),
            status.BoundSourceCount,
            status.LiveSourceCount,
            status.FrozenSourceCount,
            status.ReconnectingSourceCount,
            status.ConfiguredWidth,
            status.ConfiguredHeight,
            status.TargetFps,
            status.RenderFpsMilli,
            status.LatestComposedFrameSequence,
            status.LatestComposedFrameAgeMs,
            status.OutputConsumerCount);
    }

    public ViewSourceRuntimeStatus GetSourceStatus(uint slotIndex)
    {
        ThrowIfUnavailable();
        RuntimeGuard.EnsureSuccess(
            $"Querying View '{Definition.Id}' slot {slotIndex} status",
            _nativeView.TryGetSourceStatus(slotIndex, out var status));
        return new ViewSourceRuntimeStatus(
            status.SlotIndex,
            MapSourceState(status.State),
            status.HasBinding,
            status.CameraId,
            status.SourceLive,
            status.FreezeCacheHasFrame);
    }

    public ViewPreviewRuntime AttachPreview(PreviewHostSurface host)
    {
        ThrowIfUnavailable();
        return _owner.AttachPreview(this, host.Validate());
    }

    public void Dispose()
    {
        _owner.DisposeView(this);
        GC.SuppressFinalize(this);
    }

    internal void DisposeOwned()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _nativeView.Dispose();
    }

    private void ThrowIfUnavailable()
    {
        _owner.ThrowIfDisposed();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static ViewRuntimeState MapState(NativeViewState state)
        => state switch
        {
            NativeViewState.Stopped => ViewRuntimeState.Stopped,
            NativeViewState.Running => ViewRuntimeState.Running,
            _ => throw new InvalidOperationException($"Unknown native View state {(uint)state}."),
        };

    private static ViewSourceRuntimeState MapSourceState(NativeViewSourceState state)
        => state switch
        {
            NativeViewSourceState.Unbound => ViewSourceRuntimeState.Unbound,
            NativeViewSourceState.WaitingForFirstFrame => ViewSourceRuntimeState.WaitingForFirstFrame,
            NativeViewSourceState.Live => ViewSourceRuntimeState.Live,
            NativeViewSourceState.FrozenLastGood => ViewSourceRuntimeState.FrozenLastGood,
            NativeViewSourceState.Reconnecting => ViewSourceRuntimeState.Reconnecting,
            NativeViewSourceState.MissingOrStale => ViewSourceRuntimeState.MissingOrStale,
            _ => throw new InvalidOperationException($"Unknown native View source state {(uint)state}."),
        };

    internal static NativeCameraElementConfig ToNative(CameraElementDefinition element)
        => new(
            element.Id,
            element.CameraId,
            element.X,
            element.Y,
            element.Width,
            element.Height,
            element.ZOrder,
            element.CropLeft,
            element.CropTop,
            element.CropRight,
            element.CropBottom,
            element.RotationDegrees,
            element.FlipHorizontal,
            element.FlipVertical,
            element.Visible,
            element.Enabled,
            element.FitMode switch
            {
                CameraElementFitMode.Stretch => NativeCameraElementFitMode.Stretch,
                CameraElementFitMode.Contain => NativeCameraElementFitMode.Contain,
                CameraElementFitMode.Cover => NativeCameraElementFitMode.Cover,
                _ => throw new ArgumentOutOfRangeException(nameof(element)),
            });
}
