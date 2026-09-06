using RoboCamHub.Domain;

namespace RoboCamHub.Runtime.Tests;

public sealed class ShowPersistenceStartupTests
{
    [Fact]
    public void CompleteGraphIsCreatedBeforeEnabledCamerasAndOutputsStart()
    {
        var factory = new RecordingNativeRuntimeFactory();
        var enabledCamera = new CameraDefinition("camera-on", "On", "rtsp://10.0.0.1/stream");
        var disabledCamera = new CameraDefinition("camera-off", "Off", "rtsp://10.0.0.2/stream", enabled: false);
        var view = new ViewDefinition("view", "View", "camera-on", "camera-off");
        var enabledOutput = new OutputDefinition("output-on", "On", "ROBOCAM - ON", "view");
        var disabledOutput = new OutputDefinition("output-off", "Off", "ROBOCAM - OFF", "view", enabled: false);
        var show = new ShowDefinition(
            "show", "Show", [enabledCamera, disabledCamera], [view], [enabledOutput, disabledOutput]);

        using var runtime = ShowRuntime.Create(show, factory);

        var events = factory.Events;
        var finalConfiguration = events.IndexOf("sender:create:view:ROBOCAM - OFF");
        Assert.True(finalConfiguration >= 0);
        Assert.True(events.IndexOf("camera:start:camera-on") > finalConfiguration);
        Assert.True(events.IndexOf("sender:start:ROBOCAM - ON") > finalConfiguration);
        Assert.DoesNotContain("camera:start:camera-off", events);
        Assert.DoesNotContain("sender:start:ROBOCAM - OFF", events);
    }

    [Fact]
    public void ConfigurationFailureDisposesCandidateWithoutStartingAnything()
    {
        var factory = new RecordingNativeRuntimeFactory();
        var camera = new CameraDefinition("camera", "Camera", "rtsp://10.0.0.1/stream");
        var view = new ViewDefinition("view", "View", "camera");
        var duplicateNames = new[]
        {
            new OutputDefinition("one", "One", "ROBOCAM - SAME", "view"),
            new OutputDefinition("two", "Two", "ROBOCAM - SAME", "view"),
        };
        var show = new ShowDefinition("show", "Show", [camera], [view], duplicateNames);

        Assert.Throws<InvalidOperationException>(() => ShowRuntime.Create(show, factory));

        Assert.DoesNotContain(factory.Events, item => item.StartsWith("camera:start:", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Events, item => item.StartsWith("sender:start:", StringComparison.Ordinal));
        Assert.Contains("engine:dispose", factory.Events);
    }
}
