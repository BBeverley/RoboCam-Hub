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
    public void ExplicitSceneIsAppliedThroughRuntimeWithoutCreatingCameraOwnership()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        var camera = show.AddCamera(Camera("camera-1"));
        camera.Start();
        var definition = new ViewDefinition(
            "view-scene",
            "Freeform",
            new ViewSceneElementDefinition[]
            {
                new CameraElementDefinition("large", "camera-1", 0, 0, 0.75, 1, zOrder: 0),
                new CameraElementDefinition(
                    "inset",
                    "camera-1",
                    0.7,
                    0.05,
                    0.25,
                    0.25,
                    zOrder: 10,
                    cropLeft: 0.1,
                    rotationDegrees: 15,
                    flipHorizontal: true),
            });

        var view = show.AddView(definition);
        view.ApplyScene(definition.SceneElements.Reverse().ToArray());
        var diagnostics = show.GetDiagnostics();

        Assert.NotSame(definition, view.Definition);
        Assert.Equal(
            new[] { "inset", "large" },
            view.Definition.SceneElements.Select(element => element.Id));
        Assert.Equal((uint)2, view.GetStatus().BoundSourceCount);
        Assert.Equal((uint)1, diagnostics.ConfiguredCameraCount);
        Assert.Equal((uint)1, diagnostics.ActiveRtspSessionTotal);
        Assert.Equal((uint)1, diagnostics.ActiveDecoderTotal);
        Assert.Equal((uint)1, diagnostics.ViewCount);
        Assert.Equal((uint)2, diagnostics.TotalBoundViewSourceCount);
        Assert.Equal(2, factory.Events.Count(item => item == "view:apply-scene:view-scene:2"));
        Assert.DoesNotContain(factory.Events, item => item.StartsWith("view:bind:view-scene", StringComparison.Ordinal));

        var appliedDefinition = view.Definition;
        Assert.Throws<ArgumentException>(() => view.ApplyScene(
            new ViewSceneElementDefinition[]
            {
                appliedDefinition.SceneElements[0],
                appliedDefinition.SceneElements[0],
            }));
        Assert.Same(appliedDefinition, view.Definition);
        Assert.Equal(2, factory.Events.Count(item => item == "view:apply-scene:view-scene:2"));
    }

    [Fact]
    public void ExplicitSceneMissingCameraFailsBeforeNativeViewCreationOrApply()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        var definition = new ViewDefinition(
            "view-missing-scene",
            "Missing",
            new ViewSceneElementDefinition[]
            {
                new CameraElementDefinition("missing-element", "missing-camera", 0, 0, 1, 1),
            });

        var error = Assert.Throws<RuntimeReferenceException>(() => show.AddView(definition));

        Assert.Contains("missing-element", error.Message, StringComparison.Ordinal);
        Assert.Contains("missing-camera", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Events, item => item.StartsWith("view:create", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Events, item => item.StartsWith("view:apply-scene", StringComparison.Ordinal));
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
    public void MultipleViewsAndOutputsPreserveStableFanOutOwnership()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        var camera = show.AddCamera(Camera("shared-camera"));
        camera.Start();
        var viewA = show.AddView(new ViewDefinition(
            "view-a",
            "Spots A",
            "shared-camera",
            "shared-camera",
            "shared-camera",
            "shared-camera"));
        var viewB = show.AddView(new ViewDefinition(
            "view-b",
            "Spots B",
            "shared-camera",
            "shared-camera",
            "shared-camera",
            "shared-camera"));
        var outputA = show.AddOutput(new OutputDefinition(
            "output-a",
            "Output A",
            "ROBOCAM - A",
            viewA.Definition.Id));
        var outputB = show.AddOutput(new OutputDefinition(
            "output-b",
            "Output B",
            "ROBOCAM - B",
            viewB.Definition.Id));
        var outputA2 = show.AddOutput(new OutputDefinition(
            "output-a-backup",
            "Output A Backup",
            "ROBOCAM - A BACKUP",
            viewA.Definition.Id));

        outputA.Start();
        outputB.Start();
        outputA2.Start();
        var diagnostics = show.GetDiagnostics();

        Assert.Equal(2, show.Views.Count);
        Assert.Equal(3, show.Outputs.Count);
        Assert.Equal((uint)1, diagnostics.ConfiguredCameraCount);
        Assert.Equal((uint)1, diagnostics.ActiveRtspSessionTotal);
        Assert.Equal((uint)1, diagnostics.ActiveDecoderTotal);
        Assert.Equal((uint)2, diagnostics.ViewCount);
        Assert.Equal((uint)8, diagnostics.TotalBoundViewSourceCount);
        Assert.Equal((uint)2, viewA.GetStatus().OutputConsumerCount);
        Assert.Equal((uint)1, viewB.GetStatus().OutputConsumerCount);

        outputA.Stop();
        outputA.Dispose();

        Assert.Equal(OutputRuntimeState.Running, outputB.GetStatus().State);
        Assert.Equal(OutputRuntimeState.Running, outputA2.GetStatus().State);
        Assert.False(viewA.IsDisposed);
        Assert.Equal((uint)1, viewA.GetStatus().OutputConsumerCount);
        Assert.Equal((uint)1, show.GetDiagnostics().ActiveRtspSessionTotal);
        Assert.Equal((uint)1, show.GetDiagnostics().ActiveDecoderTotal);
    }

    [Fact]
    public void DestroyingOneViewDisposesOnlyItsDependentOutputs()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        var viewA = show.AddView(new ViewDefinition("view-a", "Spots A"));
        var viewB = show.AddView(new ViewDefinition("view-b", "Spots B"));
        var outputA = show.AddOutput(new OutputDefinition(
            "output-a",
            "Output A",
            "ROBOCAM - A",
            viewA.Definition.Id));
        var outputB = show.AddOutput(new OutputDefinition(
            "output-b",
            "Output B",
            "ROBOCAM - B",
            viewB.Definition.Id));
        outputA.Start();
        outputB.Start();

        viewA.Dispose();

        Assert.True(viewA.IsDisposed);
        Assert.True(outputA.IsDisposed);
        Assert.False(viewB.IsDisposed);
        Assert.False(outputB.IsDisposed);
        Assert.Equal(OutputRuntimeState.Running, outputB.GetStatus().State);
        Assert.Single(show.Views);
        Assert.Single(show.Outputs);
        Assert.Same(viewB, show.GetView("view-b"));
        Assert.Same(outputB, show.GetOutput("output-b"));
    }

    [Fact]
    public void DuplicateNdiSourceNamesAreRejectedBeforeNativeSenderCreation()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        var view = show.AddView(new ViewDefinition("view-main", "Main"));
        show.AddOutput(new OutputDefinition(
            "output-a",
            "Output A",
            "ROBOCAM - MAIN",
            view.Definition.Id));

        var exception = Assert.Throws<InvalidOperationException>(() => show.AddOutput(
            new OutputDefinition(
                "output-b",
                "Output B",
                "robocam - main",
                view.Definition.Id)));

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
        Assert.Single(show.Outputs);
        Assert.Equal(1, factory.Events.Count(item => item.StartsWith("sender:create", StringComparison.Ordinal)));
    }

    [Fact]
    public void PreviewRuntimeAttachesToExistingViewWithoutChangingMediaOwnership()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = CreateCompleteRuntime(factory, start: true);
        var before = show.GetDiagnostics();
        var view = show.GetView("view-main");

        using var preview = view.AttachPreview(new PreviewHostSurface(
            PreviewHostPlatform.MacOSNsView,
            42,
            30));
        var status = preview.GetStatus();
        var after = show.GetDiagnostics();

        Assert.Equal(ViewPreviewRuntimeState.Live, status.State);
        Assert.True(status.Attached);
        Assert.Equal("view-main", status.ViewId);
        Assert.Equal((uint)30_000, status.PresentationFpsMilli);
        Assert.Equal(before.ActiveRtspSessionTotal, after.ActiveRtspSessionTotal);
        Assert.Equal(before.ActiveDecoderTotal, after.ActiveDecoderTotal);
        Assert.Equal(before.ViewCount, after.ViewCount);
        Assert.Single(show.Previews);
        Assert.Contains("preview:create:view-main:MacOSNsView:30", factory.Events);
    }

    [Fact]
    public void PreviewCanAttachAndDetachRepeatedlyWithoutRetainingOwnership()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        var view = show.AddView(new ViewDefinition("view-main", "Main"));

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var preview = view.AttachPreview(new PreviewHostSurface(
                PreviewHostPlatform.WindowsHwnd,
                42,
                30));
            Assert.Single(show.Previews);
            preview.Dispose();
            Assert.Empty(show.Previews);
        }

        Assert.Equal(100, factory.Events.Count(item => item == "preview:dispose:view-main"));
    }

    [Fact]
    public void PreviewCanSwitchViewsWithoutRetainingThePreviousAttachment()
    {
        var factory = new RecordingNativeRuntimeFactory();
        using var show = ShowRuntime.Create(factory);
        var first = show.AddView(new ViewDefinition("view-one", "One"));
        var second = show.AddView(new ViewDefinition("view-two", "Two"));
        var host = new PreviewHostSurface(PreviewHostPlatform.MacOSNsView, 42, 30);

        var firstPreview = first.AttachPreview(host);
        firstPreview.Dispose();
        using var secondPreview = second.AttachPreview(host);

        Assert.True(firstPreview.IsDisposed);
        Assert.False(secondPreview.IsDisposed);
        Assert.Same(second, secondPreview.View);
        Assert.Single(show.Previews);
        AssertOrder(
            factory.Events,
            "preview:create:view-one:MacOSNsView:30",
            "preview:dispose:view-one",
            "preview:create:view-two:MacOSNsView:30");
    }

    [Fact]
    public void ViewAndShowDisposalReleasePreviewBeforeDependentNativeOwners()
    {
        var viewFactory = new RecordingNativeRuntimeFactory();
        using (var show = CreateCompleteRuntime(viewFactory, start: true))
        {
            var view = show.GetView("view-main");
            var preview = view.AttachPreview(new PreviewHostSurface(
                PreviewHostPlatform.MacOSNsView,
                42,
                30));

            view.Dispose();

            Assert.True(preview.IsDisposed);
            Assert.Empty(show.Previews);
            AssertOrder(
                viewFactory.Events,
                "preview:dispose:view-main",
                "sender:dispose:ROBOCAM - MAIN",
                "view:dispose:view-main");
        }

        var showFactory = new RecordingNativeRuntimeFactory();
        var wholeShow = CreateCompleteRuntime(showFactory, start: true);
        wholeShow.GetView("view-main").AttachPreview(new PreviewHostSurface(
            PreviewHostPlatform.WindowsHwnd,
            42,
            30));

        wholeShow.Dispose();

        AssertOrder(
            showFactory.Events,
            "preview:dispose:view-main",
            "sender:dispose:ROBOCAM - MAIN",
            "view:dispose:view-main",
            "engine:dispose");
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
