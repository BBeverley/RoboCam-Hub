using System.Reflection;
using RoboCamHub.Domain;
using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime.Tests;

public sealed class ShowRuntimeTests
{
    [Fact]
    public void CameraRuntimeMapsDefinitionAndOperationsToNativeInteropBoundary()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        var definition = Camera("camera-1", timeoutMs: 750);

        var camera = show.AddCamera(definition);
        camera.Start();
        var status = camera.GetStatus();
        camera.Stop();

        Assert.Same(definition, camera.Definition);
        Assert.Equal(CameraRuntimeState.Receiving, status.State);
        Assert.Equal((uint)1, status.ActiveRtspSessionCount);
        Assert.Equal((uint)1, status.ActiveDecoderCount);
        Assert.Contains("camera:add:camera-1:rtsp://127.0.0.1:1/profile2/media.smp:750", factory.Events);
        Assert.Contains("camera:start:camera-1", factory.Events);
        Assert.Contains("camera:status:camera-1", factory.Events);
        Assert.Contains("camera:stop:camera-1", factory.Events);
    }

    [Fact]
    public void DuplicateLogicalCameraIsRejectedBeforeCreatingNativeOwnership()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        show.AddCamera(Camera("camera-1")).Start();

        var exception = Assert.Throws<InvalidOperationException>(() => show.AddCamera(
            new CameraDefinition(
                "camera-1",
                "Renamed Camera",
                "rtsp://127.0.0.1:2/profile2/media.smp")));

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, factory.Engine!.AddCameraCallCount);
        Assert.Equal((uint)1, show.GetDiagnostics().ConfiguredCameraCount);
        Assert.Equal((uint)1, show.GetDiagnostics().ActiveRtspSessionTotal);
        Assert.Equal((uint)1, show.GetDiagnostics().ActiveDecoderTotal);
    }

    [Fact]
    public void ViewRuntimeResolvesAllAssignmentsByLogicalCameraId()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        for (var index = 1; index <= 4; index++)
        {
            show.AddCamera(Camera($"camera-{index}"));
        }

        var view = show.AddView(MainView());

        Assert.Equal("camera-1", view.GetSourceStatus(0).CameraId);
        Assert.Equal("camera-4", view.GetSourceStatus(3).CameraId);
        Assert.Equal((uint)4, view.GetStatus().BoundSourceCount);
        Assert.Equal((uint)4, show.GetDiagnostics().TotalBoundViewSourceCount);
        Assert.Contains("view:bind:view-main:2:camera-3", factory.Events);

        view.UnbindSource(2);
        Assert.False(view.GetSourceStatus(2).HasBinding);
        view.BindCameraSource(2, "camera-4");
        Assert.Equal("camera-4", view.GetSourceStatus(2).CameraId);
    }

    [Fact]
    public void OutputRuntimeResolvesViewAndControlsSenderWithoutPublicNativeHandles()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        for (var index = 1; index <= 4; index++)
        {
            show.AddCamera(Camera($"camera-{index}"));
        }

        var view = show.AddView(MainView());
        var output = show.AddOutput(MainOutput());
        output.Start();

        Assert.Same(view, output.View);
        Assert.Equal(OutputRuntimeState.Running, output.GetStatus().State);
        Assert.Contains("sender:create:view-main:ROBOCAM - MAIN", factory.Events);
        Assert.Contains("sender:start:ROBOCAM - MAIN", factory.Events);

        var nativeNamespace = typeof(NativeEngine).Namespace;
        var publicApiTypes = typeof(ShowRuntime).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetProperties().Select(property => property.PropertyType)
                .Concat(type.GetMethods().Select(method => method.ReturnType)));
        Assert.DoesNotContain(publicApiTypes, type => type.Namespace == nativeNamespace);
    }

    [Fact]
    public void MissingLogicalReferencesFailBeforeNativeCreation()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);

        var viewError = Assert.Throws<RuntimeReferenceException>(() => show.AddView(
            new ViewDefinition("view-missing", "Missing", "unknown-camera")));
        Assert.Contains("slot 0", viewError.Message, StringComparison.Ordinal);
        Assert.Contains("unknown-camera", viewError.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Events, item => item.StartsWith("view:create", StringComparison.Ordinal));

        var outputError = Assert.Throws<RuntimeReferenceException>(() => show.AddOutput(
            new OutputDefinition("output-missing", "Missing", "ROBOCAM - MISSING", "unknown-view")));
        Assert.Contains("unknown-view", outputError.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Events, item => item.StartsWith("sender:create", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposingViewFirstDisposesDependentOutputDeterministically()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = CreateCompleteRuntime(factory, start: true);
        var view = show.GetView("view-main");
        var output = show.GetOutput("output-main");

        view.Dispose();

        Assert.True(view.IsDisposed);
        Assert.True(output.IsDisposed);
        Assert.Empty(show.Views);
        Assert.Empty(show.Outputs);
        AssertOrder(
            factory.Events,
            "sender:stop:ROBOCAM - MAIN",
            "sender:dispose:ROBOCAM - MAIN",
            "view:dispose:view-main");
    }

    [Fact]
    public void ShowDisposalUsesOutputViewCameraEngineDependencyOrder()
    {
        var factory = new RecordingNativeRuntimeFactory();
        var show = CreateCompleteRuntime(factory, start: true);

        show.Dispose();

        Assert.True(show.IsDisposed);
        AssertOrder(
            factory.Events,
            "sender:dispose:ROBOCAM - MAIN",
            "view:dispose:view-main",
            "camera:stop:camera-4",
            "camera:remove:camera-4",
            "engine:dispose");
    }

    [Fact]
    public void RepeatedCreateAndDisposeCyclesAreSafe()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var factory = new RecordingNativeRuntimeFactory();
            var show = CreateCompleteRuntime(factory, start: true);

            show.Dispose();
            show.Dispose();

            Assert.True(show.IsDisposed);
            Assert.Equal(1, factory.Events.Count(item => item == "engine:dispose"));
            Assert.Equal(1, factory.Events.Count(item => item == "view:dispose:view-main"));
            Assert.Equal(1, factory.Events.Count(item => item == "sender:dispose:ROBOCAM - MAIN"));
        }
    }

    [Fact]
    public void DesiredEnabledStateIsSeparateFromActualRuntimeState()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        var camera = show.AddCamera(new CameraDefinition(
            "camera-disabled",
            "Disabled",
            "rtsp://127.0.0.1:1/profile2/media.smp",
            enabled: false));

        Assert.False(camera.Definition.Enabled);
        Assert.Equal(CameraRuntimeState.Stopped, camera.GetStatus().State);
        Assert.Throws<InvalidOperationException>(camera.Start);
        Assert.DoesNotContain("camera:start:camera-disabled", factory.Events);

        show.AddCamera(Camera("camera-1"));
        var view = show.AddView(new ViewDefinition("view-main", "Main", "camera-1"));
        var output = show.AddOutput(new OutputDefinition(
            "output-disabled",
            "Disabled Output",
            "ROBOCAM - DISABLED",
            view.Definition.Id,
            enabled: false));

        Assert.False(output.Definition.Enabled);
        Assert.Equal(OutputRuntimeState.Stopped, output.GetStatus().State);
        Assert.Throws<InvalidOperationException>(output.Start);
        Assert.DoesNotContain("sender:start:ROBOCAM - DISABLED", factory.Events);
    }

    private static ShowRuntime CreateCompleteRuntime(RecordingNativeRuntimeFactory factory, bool start)
    {
        var show = ShowRuntime.Create(factory);
        for (var index = 1; index <= 4; index++)
        {
            var camera = show.AddCamera(Camera($"camera-{index}"));
            if (start)
            {
                camera.Start();
            }
        }

        show.AddView(MainView());
        var output = show.AddOutput(MainOutput());
        if (start)
        {
            output.Start();
        }

        return show;
    }

    private static CameraDefinition Camera(string id, uint timeoutMs = 250)
        => new(
            id,
            id,
            "rtsp://127.0.0.1:1/profile2/media.smp",
            enabled: true,
            connectTimeoutMs: timeoutMs);

    private static ViewDefinition MainView()
        => new("view-main", "Main 2x2", "camera-1", "camera-2", "camera-3", "camera-4");

    private static OutputDefinition MainOutput()
        => new("output-main", "Main Output", "ROBOCAM - MAIN", "view-main", enabled: true);

    private static void AssertOrder(IReadOnlyList<string> events, params string[] expected)
    {
        var previousIndex = -1;
        foreach (var item in expected)
        {
            var index = Enumerable.Range(0, events.Count)
                .FirstOrDefault(candidate => events[candidate] == item, -1);
            Assert.True(index > previousIndex, $"Expected '{item}' after index {previousIndex}. Events: {string.Join(", ", events)}");
            previousIndex = index;
        }
    }
}
