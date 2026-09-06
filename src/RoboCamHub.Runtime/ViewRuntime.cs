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

    public void ApplyScene(
        IReadOnlyList<ViewSceneElementDefinition> elements,
        IReadOnlyList<AssetDefinition>? assets = null)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(elements);

        var updatedDefinition = new ViewDefinition(
            Definition.Id,
            Definition.Name,
            elements,
            assets ?? Definition.Assets);
        var nativeElements = ToNativeScene(updatedDefinition, _owner);

        RuntimeGuard.EnsureSuccess(
            $"Applying View '{Definition.Id}' scene",
            _nativeView.ApplyScene(nativeElements));
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

    internal static IReadOnlyList<NativeSceneElementConfig> ToNativeScene(
        ViewDefinition definition,
        ShowRuntime owner)
    {
        var assets = definition.Assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        return definition.SceneElements.Select(element => element switch
        {
            CameraElementDefinition camera => Camera(camera, owner),
            TextElementDefinition text => Text(text),
            ImageElementDefinition image => Image(image, assets),
            ShapeElementDefinition rectangle => Rectangle(rectangle),
            FrameElementDefinition frame => Frame(frame),
            _ => throw new NotSupportedException($"Scene element type '{element.GetType().Name}' is unsupported."),
        }).ToArray();
    }

    private static NativeSceneElementConfig Common(
        ViewSceneElementDefinition element,
        NativeSceneElementKind kind,
        double opacity = 1)
        => new(
            kind,
            element.Id,
            element.X,
            element.Y,
            element.Width,
            element.Height,
            element.ZOrder,
            element.RotationDegrees,
            element.FlipHorizontal,
            element.FlipVertical,
            element.Visible,
            element.Enabled,
            opacity);

    private static NativeSceneElementConfig Camera(CameraElementDefinition element, ShowRuntime owner)
    {
        _ = owner.GetCamera(element.CameraId);
        return Common(element, NativeSceneElementKind.Camera) with
        {
            CameraId = element.CameraId,
            FitMode = Fit(element.FitMode),
        };
    }

    private static NativeSceneElementConfig Text(TextElementDefinition element)
        => Common(element, NativeSceneElementKind.Text, 1) with
        {
            Text = element.Text,
            FontFamily = element.FontFamily,
            FontSize = element.FontSize,
            PrimaryRgba = element.TextColorRgba,
            SecondaryRgba = element.BackgroundColorRgba ?? 0,
            SecondaryEnabled = element.BackgroundColorRgba.HasValue,
            Opacity = 1,
            TextAlignment = (NativeTextAlignment)element.Alignment,
            TextWeight = (NativeTextWeight)element.Weight,
            TextStyle = (NativeTextStyle)element.Style,
        };

    private static NativeSceneElementConfig Image(
        ImageElementDefinition element,
        IReadOnlyDictionary<string, AssetDefinition> assets)
    {
        var asset = assets.TryGetValue(element.AssetId, out var found)
            ? found
            : throw new RuntimeReferenceException(
                $"Image element '{element.Id}' references missing asset '{element.AssetId}'.");
        return Common(element, NativeSceneElementKind.Image, element.Opacity) with
        {
            ImageAssetId = asset.Id,
            ImageSource = asset.RuntimeSourceReference,
            FitMode = Fit(element.FitMode),
        };
    }

    private static NativeSceneElementConfig Rectangle(ShapeElementDefinition element)
        => Common(element, NativeSceneElementKind.Rectangle, element.Opacity) with
        {
            PrimaryRgba = element.FillColorRgba,
            SecondaryRgba = element.OutlineColorRgba ?? 0,
            SecondaryEnabled = element.OutlineColorRgba.HasValue,
            StrokeWidth = element.OutlineWidth,
        };

    private static NativeSceneElementConfig Frame(FrameElementDefinition element)
        => Common(element, NativeSceneElementKind.Frame, element.Opacity) with
        {
            PrimaryRgba = element.ColorRgba,
            StrokeWidth = element.Thickness,
        };

    private static NativeCameraElementFitMode Fit(CameraElementFitMode fitMode)
        => fitMode switch
        {
            CameraElementFitMode.Stretch => NativeCameraElementFitMode.Stretch,
            CameraElementFitMode.Contain => NativeCameraElementFitMode.Contain,
            CameraElementFitMode.Cover => NativeCameraElementFitMode.Cover,
            _ => throw new ArgumentOutOfRangeException(nameof(fitMode)),
        };
}
