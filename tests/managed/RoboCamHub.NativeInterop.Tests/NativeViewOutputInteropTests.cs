namespace RoboCamHub.NativeInterop.Tests;

public sealed class NativeViewOutputInteropTests
{
    [Fact]
    public void ViewAndSenderSafeHandlesWrapExistingNativeAbi()
    {
        using var engine = NativeEngine.Create();
        Assert.Equal(NativeResult.Ok, engine.AddOrUpdateCamera(new NativeCameraConfig(
            "camera-1",
            "rtsp://127.0.0.1:1/profile2/media.smp",
            ConnectTimeoutMs: 250)));

        Assert.Equal(NativeResult.Ok, engine.TryCreateView("view-main", out var view));
        Assert.NotNull(view);
        using (view)
        {
            Assert.Equal(NativeResult.Ok, view.BindCameraSource(0, "camera-1"));
            Assert.Equal(NativeResult.Ok, view.TryGetSourceStatus(0, out var sourceStatus));
            Assert.True(sourceStatus.HasBinding);
            Assert.Equal("camera-1", sourceStatus.CameraId);

            Assert.Equal(NativeResult.Ok, view.TryGetStatus(out var viewStatus));
            Assert.Equal((uint)1, viewStatus.BoundSourceCount);
            Assert.Equal((uint)1920, viewStatus.ConfiguredWidth);
            Assert.Equal((uint)1080, viewStatus.ConfiguredHeight);
            Assert.Equal((uint)60, viewStatus.TargetFps);

            var senderName = $"ROBOCAM - Gate5A-{Guid.NewGuid():N}";
            Assert.Equal(NativeResult.Ok, view.TryCreateNdiSender(senderName, out var sender));
            Assert.NotNull(sender);
            using (sender)
            {
                Assert.Equal(NativeResult.Ok, sender.TryGetStatus(out var stoppedStatus));
                Assert.Equal(NativeNdiSenderState.Stopped, stoppedStatus.State);
                Assert.Equal(senderName, stoppedStatus.SenderName);

                Assert.Equal(NativeResult.Ok, sender.Start());
                Assert.Equal(NativeResult.Ok, sender.TryGetStatus(out var activeStatus));
                Assert.Contains(
                    activeStatus.State,
                    new[]
                    {
                        NativeNdiSenderState.Starting,
                        NativeNdiSenderState.Running,
                        NativeNdiSenderState.WaitingForViewFrame,
                    });
                Assert.Equal(NativeResult.Ok, sender.Stop());
            }

            Assert.True(sender.IsDisposed);
        }

        Assert.True(view.IsDisposed);
        Assert.Equal(NativeResult.Ok, engine.TryGetEngineDiagnostics(out var diagnostics));
        Assert.Equal((uint)0, diagnostics.ViewCount);
        Assert.Equal((uint)0, diagnostics.TotalBoundViewSourceCount);
    }

    [Fact]
    public void DisposedViewAndSenderRejectFurtherOperations()
    {
        using var engine = NativeEngine.Create();
        Assert.Equal(NativeResult.Ok, engine.TryCreateView("view-dispose", out var view));
        Assert.NotNull(view);
        Assert.Equal(NativeResult.Ok, view.TryCreateNdiSender("ROBOCAM - Gate5A-Dispose", out var sender));
        Assert.NotNull(sender);

        sender.Dispose();
        view.Dispose();

        Assert.Throws<ObjectDisposedException>(() => sender.Start());
        Assert.Throws<ObjectDisposedException>(() => sender.TryGetStatus(out _));
        Assert.Throws<ObjectDisposedException>(() => view.TryGetStatus(out _));
        Assert.Throws<ObjectDisposedException>(() => view.BindCameraSource(0, "camera-1"));
    }
}
