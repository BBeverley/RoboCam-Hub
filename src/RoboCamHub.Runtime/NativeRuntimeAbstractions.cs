using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime;

internal interface INativeRuntimeFactory
{
    INativeRuntimeEngine CreateEngine();
}

internal interface INativeRuntimeEngine : IDisposable
{
    NativeResult AddOrUpdateCamera(in NativeCameraConfig config);

    NativeResult RemoveCamera(string cameraId);

    NativeResult StartCamera(string cameraId);

    NativeResult StopCamera(string cameraId);

    NativeResult TryGetCameraStatus(string cameraId, out NativeCameraStatus status);

    NativeResult TryGetDiagnostics(out NativeEngineDiagnostics diagnostics);

    NativeResult TryCreateView(string viewId, out INativeRuntimeView? view);
}

internal interface INativeRuntimeView : IDisposable
{
    NativeResult ApplyCameraScene(IReadOnlyList<NativeCameraElementConfig> elements);

    NativeResult ApplyScene(IReadOnlyList<NativeSceneElementConfig> elements);

    NativeResult BindCameraSource(uint slotIndex, string cameraId);

    NativeResult UnbindSource(uint slotIndex);

    NativeResult TryGetStatus(out NativeViewStatus status);

    NativeResult TryGetSourceStatus(uint slotIndex, out NativeViewSourceStatus status);

    NativeResult TryCreateSender(string senderName, out INativeRuntimeSender? sender);

    NativeResult TryCreatePreview(PreviewHostSurface host, out INativeRuntimePreview? preview);
}

internal interface INativeRuntimeSender : IDisposable
{
    NativeResult Start();

    NativeResult Stop();

    NativeResult TryGetStatus(out NativeNdiSenderStatus status);
}

internal interface INativeRuntimePreview : IDisposable
{
    NativeResult TryGetStatus(out NativeViewPreviewStatus status);
}

internal sealed class NativeRuntimeFactory : INativeRuntimeFactory
{
    public INativeRuntimeEngine CreateEngine()
        => new NativeRuntimeEngine(NativeEngine.Create());
}

internal sealed class NativeRuntimeEngine(NativeEngine engine) : INativeRuntimeEngine
{
    public NativeResult AddOrUpdateCamera(in NativeCameraConfig config)
        => engine.AddOrUpdateCamera(config);

    public NativeResult RemoveCamera(string cameraId)
        => engine.RemoveCamera(cameraId);

    public NativeResult StartCamera(string cameraId)
        => engine.StartCamera(cameraId);

    public NativeResult StopCamera(string cameraId)
        => engine.StopCamera(cameraId);

    public NativeResult TryGetCameraStatus(string cameraId, out NativeCameraStatus status)
        => engine.TryGetCameraStatus(cameraId, out status);

    public NativeResult TryGetDiagnostics(out NativeEngineDiagnostics diagnostics)
        => engine.TryGetEngineDiagnostics(out diagnostics);

    public NativeResult TryCreateView(string viewId, out INativeRuntimeView? view)
    {
        var result = engine.TryCreateView(viewId, out var nativeView);
        view = result == NativeResult.Ok && nativeView is not null
            ? new NativeRuntimeView(nativeView)
            : null;
        return result;
    }

    public void Dispose() => engine.Dispose();
}

internal sealed class NativeRuntimeView(NativeView view) : INativeRuntimeView
{
    public NativeResult ApplyCameraScene(IReadOnlyList<NativeCameraElementConfig> elements)
        => view.ApplyCameraScene(elements);

    public NativeResult ApplyScene(IReadOnlyList<NativeSceneElementConfig> elements)
        => view.ApplyScene(elements);

    public NativeResult BindCameraSource(uint slotIndex, string cameraId)
        => view.BindCameraSource(slotIndex, cameraId);

    public NativeResult UnbindSource(uint slotIndex)
        => view.UnbindSource(slotIndex);

    public NativeResult TryGetStatus(out NativeViewStatus status)
        => view.TryGetStatus(out status);

    public NativeResult TryGetSourceStatus(uint slotIndex, out NativeViewSourceStatus status)
        => view.TryGetSourceStatus(slotIndex, out status);

    public NativeResult TryCreateSender(string senderName, out INativeRuntimeSender? sender)
    {
        var result = view.TryCreateNdiSender(senderName, out var nativeSender);
        sender = result == NativeResult.Ok && nativeSender is not null
            ? new NativeRuntimeSender(nativeSender)
            : null;
        return result;
    }

    public NativeResult TryCreatePreview(PreviewHostSurface host, out INativeRuntimePreview? preview)
    {
        var platform = host.Platform switch
        {
            PreviewHostPlatform.WindowsHwnd => NativeViewPreviewPlatform.WindowsHwnd,
            PreviewHostPlatform.MacOSNsView => NativeViewPreviewPlatform.MacOSNsView,
            _ => throw new ArgumentOutOfRangeException(nameof(host)),
        };
        var result = view.TryCreatePreview(
            platform,
            host.NativeHandle,
            host.TargetFps,
            out var nativePreview);
        preview = result == NativeResult.Ok && nativePreview is not null
            ? new NativeRuntimePreview(nativePreview)
            : null;
        return result;
    }

    public void Dispose() => view.Dispose();
}

internal sealed class NativeRuntimePreview(NativeViewPreview preview) : INativeRuntimePreview
{
    public NativeResult TryGetStatus(out NativeViewPreviewStatus status)
        => preview.TryGetStatus(out status);

    public void Dispose() => preview.Dispose();
}

internal sealed class NativeRuntimeSender(NativeNdiSender sender) : INativeRuntimeSender
{
    public NativeResult Start() => sender.Start();

    public NativeResult Stop() => sender.Stop();

    public NativeResult TryGetStatus(out NativeNdiSenderStatus status)
        => sender.TryGetStatus(out status);

    public void Dispose() => sender.Dispose();
}
