using RoboCamHub.Domain;

namespace RoboCamHub.Runtime.Tests;

public sealed class ShowRuntimeNativeIntegrationTests
{
    [Fact]
    public void ShowRuntimeDrivesFourCameraViewAndOutputThroughExistingAbi()
    {
        using var show = ShowRuntime.Create();
        var cameras = Enumerable.Range(1, 4)
            .Select(index => show.AddCamera(new CameraDefinition(
                $"gate5a-camera-{index}",
                $"Gate 5A Camera {index}",
                "rtsp://127.0.0.1:1/profile2/media.smp",
                enabled: true,
                connectTimeoutMs: 250)))
            .ToArray();

        foreach (var camera in cameras)
        {
            camera.Start();
            var status = camera.GetStatus();
            Assert.True(status.ActiveRtspSessionCount <= 1);
            Assert.True(status.ActiveDecoderCount <= 1);
        }

        var view = show.AddView(new ViewDefinition(
            "gate5a-view-main",
            "Gate 5A Main 2x2",
            "gate5a-camera-1",
            "gate5a-camera-2",
            "gate5a-camera-3",
            "gate5a-camera-4"));
        var output = show.AddOutput(new OutputDefinition(
            "gate5a-output-main",
            "Gate 5A Main Output",
            $"ROBOCAM - Gate5A-{Guid.NewGuid():N}",
            view.Definition.Id,
            enabled: true));

        output.Start();

        var viewStatus = view.GetStatus();
        var outputStatus = output.GetStatus();
        var diagnostics = show.GetDiagnostics();
        Assert.Equal((uint)4, viewStatus.BoundSourceCount);
        Assert.Equal((uint)4, diagnostics.ConfiguredCameraCount);
        Assert.Equal((uint)1, diagnostics.ViewCount);
        Assert.Equal((uint)4, diagnostics.TotalBoundViewSourceCount);
        Assert.True(diagnostics.ActiveRtspSessionTotal <= 4);
        Assert.True(diagnostics.ActiveDecoderTotal <= 4);
        Assert.Contains(
            outputStatus.State,
            new[]
            {
                OutputRuntimeState.Starting,
                OutputRuntimeState.Running,
                OutputRuntimeState.WaitingForViewFrame,
            });

        output.Stop();
        foreach (var camera in cameras)
        {
            camera.Stop();
        }
    }
}
