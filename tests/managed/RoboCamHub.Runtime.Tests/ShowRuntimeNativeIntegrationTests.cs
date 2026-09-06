using RoboCamHub.Domain;

namespace RoboCamHub.Runtime.Tests;

public sealed class ShowRuntimeNativeIntegrationTests
{
    [Fact]
    public void ShowRuntimeDrivesSharedCamerasMultipleViewsAndOutputsThroughExistingAbi()
    {
        using var show = ShowRuntime.Create();
        var cameras = Enumerable.Range(1, 4)
            .Select(index => show.AddCamera(new CameraDefinition(
                $"gate5d-camera-{index}",
                $"Gate 5D Camera {index}",
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
            "gate5d-view-a",
            "Gate 5D Spots A",
            "gate5d-camera-1",
            "gate5d-camera-2",
            "gate5d-camera-3",
            "gate5d-camera-4"));
        var viewB = show.AddView(new ViewDefinition(
            "gate5d-view-b",
            "Gate 5D Spots B",
            "gate5d-camera-4",
            "gate5d-camera-3",
            "gate5d-camera-2",
            "gate5d-camera-1"));
        var outputA = show.AddOutput(new OutputDefinition(
            "gate5d-output-a",
            "Gate 5D Output A",
            $"ROBOCAM - Gate5D-A-{Guid.NewGuid():N}",
            view.Definition.Id,
            enabled: true));
        var outputB = show.AddOutput(new OutputDefinition(
            "gate5d-output-b",
            "Gate 5D Output B",
            $"ROBOCAM - Gate5D-B-{Guid.NewGuid():N}",
            viewB.Definition.Id,
            enabled: true));
        var outputABackup = show.AddOutput(new OutputDefinition(
            "gate5d-output-a-backup",
            "Gate 5D Output A Backup",
            $"ROBOCAM - Gate5D-A-Backup-{Guid.NewGuid():N}",
            view.Definition.Id,
            enabled: true));

        outputA.Start();
        outputB.Start();
        outputABackup.Start();

        var viewStatus = view.GetStatus();
        var viewBStatus = viewB.GetStatus();
        var outputAStatus = outputA.GetStatus();
        var outputBStatus = outputB.GetStatus();
        var diagnostics = show.GetDiagnostics();
        Assert.Equal((uint)4, viewStatus.BoundSourceCount);
        Assert.Equal((uint)4, viewBStatus.BoundSourceCount);
        Assert.Equal((uint)2, viewStatus.OutputConsumerCount);
        Assert.Equal((uint)1, viewBStatus.OutputConsumerCount);
        Assert.Equal((uint)4, diagnostics.ConfiguredCameraCount);
        Assert.Equal((uint)2, diagnostics.ViewCount);
        Assert.Equal((uint)8, diagnostics.TotalBoundViewSourceCount);
        Assert.True(diagnostics.ActiveRtspSessionTotal <= 4);
        Assert.True(diagnostics.ActiveDecoderTotal <= 4);
        Assert.Contains(
            outputAStatus.State,
            new[]
            {
                OutputRuntimeState.Starting,
                OutputRuntimeState.Running,
                OutputRuntimeState.WaitingForViewFrame,
            });
        Assert.Contains(
            outputBStatus.State,
            new[]
            {
                OutputRuntimeState.Starting,
                OutputRuntimeState.Running,
                OutputRuntimeState.WaitingForViewFrame,
            });

        outputA.Stop();
        Assert.NotEqual(OutputRuntimeState.Stopped, outputB.GetStatus().State);
        Assert.NotEqual(OutputRuntimeState.Stopped, outputABackup.GetStatus().State);
        outputB.Stop();
        outputABackup.Stop();
        foreach (var camera in cameras)
        {
            camera.Stop();
        }
    }
}
