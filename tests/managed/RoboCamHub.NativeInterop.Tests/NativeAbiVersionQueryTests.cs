namespace RoboCamHub.NativeInterop.Tests;

public sealed class NativeInteropIntegrationTests
{
    [Fact]
    public void NativeLibraryLoadsAndReportsSupportedAbiVersion()
    {
        INativeAbiVersionQuery query = new NativeAbiVersionQuery();

        Assert.Equal(NativeAbiVersion.Supported, query.GetVersion());
    }

    [Fact]
    public void EngineCanBeCreatedAndDisposed()
    {
        var engine = NativeEngine.Create();

        Assert.False(engine.IsDisposed);

        engine.Dispose();

        Assert.True(engine.IsDisposed);
    }

    [Fact]
    public void AbiMismatchFailsDeterministically()
    {
        var incompatibleVersion = new NativeAbiVersion(2, 0);
        var query = new StubVersionQuery(incompatibleVersion);

        var exception = Assert.Throws<NativeAbiMismatchException>(() => NativeEngine.Create(query));

        Assert.Equal(NativeAbiVersion.Supported, exception.Expected);
        Assert.Equal(incompatibleVersion, exception.Actual);
        Assert.Equal(
            "Native ABI version mismatch. Expected 1.10, but loaded 2.0.",
            exception.Message);
    }

    [Fact]
    public void RepeatedCreateAndDisposeDoesNotLeaveOpenManagedHandles()
    {
        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var engine = NativeEngine.Create();

            engine.Dispose();
            engine.Dispose();

            Assert.True(engine.IsDisposed);
        }
    }

    [Fact]
    public void MultiCameraInteropSupportsLifecycleEnumerationAndDiagnostics()
    {
        using var engine = NativeEngine.Create();

        Assert.Equal(NativeResult.Ok, engine.TryEnumerateCameraIds(out var emptyIds));
        Assert.Empty(emptyIds);

        var addResultB = engine.AddOrUpdateCamera(new NativeCameraConfig(
            CameraId: "cam-b",
            RtspUrl: "rtsp://127.0.0.1:1/profile2/media.smp",
            ConnectTimeoutMs: 250));
        var addResultA = engine.AddOrUpdateCamera(new NativeCameraConfig(
            CameraId: "cam-a",
            RtspUrl: "rtsp://127.0.0.1:1/profile2/media.smp",
            ConnectTimeoutMs: 250));
        var addResultC = engine.AddOrUpdateCamera(new NativeCameraConfig(
            CameraId: "cam-c",
            RtspUrl: "rtsp://127.0.0.1:1/profile2/media.smp",
            ConnectTimeoutMs: 250));

        Assert.Equal(NativeResult.Ok, addResultA);
        Assert.Equal(NativeResult.Ok, addResultB);
        Assert.Equal(NativeResult.Ok, addResultC);

        Assert.Equal(NativeResult.Ok, engine.TryEnumerateCameraIds(out var sortedIds));
        Assert.Equal(new[] { "cam-a", "cam-b", "cam-c" }, sortedIds);

        var duplicateAddResult = engine.AddOrUpdateCamera(new NativeCameraConfig(
            CameraId: "cam-a",
            RtspUrl: "rtsp://127.0.0.1:2/profile2/media.smp",
            ConnectTimeoutMs: 500));
        Assert.Equal(NativeResult.Ok, duplicateAddResult);
        Assert.Equal(NativeResult.Ok, engine.TryEnumerateCameraIds(out var idsAfterDuplicate));
        Assert.Equal(new[] { "cam-a", "cam-b", "cam-c" }, idsAfterDuplicate);

        Assert.Equal(NativeResult.Ok, engine.StartCamera("cam-a"));
        Assert.Equal(NativeResult.Ok, engine.StartCamera("cam-c"));

        Thread.Sleep(200);

        Assert.Equal(NativeResult.Ok, engine.TryGetEngineDiagnostics(out var diagnostics));
        Assert.Equal((uint)3, diagnostics.ConfiguredCameraCount);
        Assert.True(diagnostics.ActiveRtspSessionTotal <= diagnostics.ConfiguredCameraCount);
        Assert.True(diagnostics.ActiveDecoderTotal <= diagnostics.ConfiguredCameraCount);

        Assert.Equal(NativeResult.Ok, engine.RemoveCamera("cam-b"));
        Assert.Equal(NativeResult.NotConfigured, engine.StartCamera("cam-b"));
        Assert.Equal(NativeResult.NotConfigured, engine.StopCamera("cam-b"));
        Assert.Equal(NativeResult.NotConfigured, engine.TryGetCameraStatus("cam-b", out _));

        Assert.Equal(NativeResult.Ok, engine.AddOrUpdateCamera(new NativeCameraConfig(
            CameraId: "cam-b",
            RtspUrl: "rtsp://127.0.0.1:1/profile2/media.smp",
            ConnectTimeoutMs: 250)));
        Assert.Equal(NativeResult.Ok, engine.TryEnumerateCameraIds(out var idsAfterReAdd));
        Assert.Equal(new[] { "cam-a", "cam-b", "cam-c" }, idsAfterReAdd);
    }

    [Fact]
    public void DisposedEngineRejectsGate2bOperations()
    {
        var engine = NativeEngine.Create();
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.AddOrUpdateCamera(new NativeCameraConfig(
            CameraId: "cam-a",
            RtspUrl: "rtsp://127.0.0.1:1/profile2/media.smp")));
        Assert.Throws<ObjectDisposedException>(() => engine.RemoveCamera("cam-a"));
        Assert.Throws<ObjectDisposedException>(() => engine.StartCamera("cam-a"));
        Assert.Throws<ObjectDisposedException>(() => engine.StopCamera("cam-a"));
        Assert.Throws<ObjectDisposedException>(() => engine.TryGetCameraStatus("cam-a", out _));
        Assert.Throws<ObjectDisposedException>(() => engine.TryEnumerateCameraIds(out _));
        Assert.Throws<ObjectDisposedException>(() => engine.TryGetEngineDiagnostics(out _));
    }

    private sealed class StubVersionQuery(NativeAbiVersion version) : INativeAbiVersionQuery
    {
        public NativeAbiVersion GetVersion() => version;
    }
}
