using System.Reflection;
using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application.Tests;

public sealed class WorkspaceViewModelTests
{
    [Theory]
    [InlineData(CameraRuntimeState.Receiving, "Receiving", "●")]
    [InlineData(CameraRuntimeState.Starting, "Starting", "◐")]
    [InlineData(CameraRuntimeState.WaitingToRetry, "Waiting to Retry", "▲")]
    [InlineData(CameraRuntimeState.Failed, "Failed", "✕")]
    [InlineData(CameraRuntimeState.Stopped, "Stopped", "○")]
    public async Task CameraRuntimeStateMapsToOperatorState(
        CameraRuntimeState state,
        string expectedText,
        string expectedIcon)
    {
        var camera = Camera("camera-1", "Spot 1");
        var runtime = new FakeWorkspaceRuntimeService([camera]);
        runtime.CameraStates[camera.Id] = state;
        await using var workspace = new WorkspaceViewModel(runtime);

        await workspace.RefreshNowAsync();

        Assert.Equal(state, workspace.Cameras[0].State);
        Assert.Equal(expectedText, workspace.Cameras[0].StateText);
        Assert.Equal(expectedIcon, workspace.Cameras[0].HealthIcon);
        Assert.False(string.IsNullOrWhiteSpace(workspace.Cameras[0].HealthColor));
        var expectedOwnership = state == CameraRuntimeState.Receiving ? 1U : 0U;
        Assert.Equal(expectedOwnership, workspace.Cameras[0].ActiveRtspSessionCount);
        Assert.Equal(expectedOwnership, workspace.Cameras[0].ActiveDecoderCount);
        Assert.Equal(expectedOwnership, workspace.ActiveRtspSessionTotal);
        Assert.Equal(expectedOwnership, workspace.ActiveDecoderTotal);
    }

    [Fact]
    public async Task CameraStartStopEnablementPreventsInvalidAndReentrantOperations()
    {
        var camera = Camera("camera-1", "Spot 1");
        var runtime = new FakeWorkspaceRuntimeService([camera]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StartCameraHandler = async () =>
        {
            entered.SetResult();
            await release.Task;
        };
        await using var workspace = new WorkspaceViewModel(runtime);
        var item = workspace.Cameras[0];

        Assert.True(item.StartCommand.CanExecute(null));
        Assert.False(item.StopCommand.CanExecute(null));
        var firstStart = item.StartCommand.ExecuteAsync();
        await entered.Task;
        Assert.True(item.IsBusy);
        Assert.False(item.StartCommand.CanExecute(null));
        Assert.False(item.StopCommand.CanExecute(null));

        await item.StartCommand.ExecuteAsync();
        Assert.Equal(1, runtime.StartCameraCallCount);
        release.SetResult();
        await firstStart;

        Assert.Equal(CameraRuntimeState.Starting, item.State);
        Assert.False(item.StartCommand.CanExecute(null));
        Assert.True(item.StopCommand.CanExecute(null));
        await item.StopCommand.ExecuteAsync();
        Assert.Equal(CameraRuntimeState.Stopped, item.State);
        Assert.True(item.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task SlotAssignmentChangesOnlyAfterRuntimeSuccess()
    {
        var cameras = new[] { Camera("camera-1", "Spot 1"), Camera("camera-2", "Spot 2") };
        var runtime = new FakeWorkspaceRuntimeService(
            cameras,
            new ViewDefinition("view-main", "Main", "camera-1"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.BindHandler = async () =>
        {
            entered.SetResult();
            await release.Task;
        };
        await using var workspace = new WorkspaceViewModel(runtime);
        var slot = workspace.SelectedView.Slots[0];
        slot.SelectedCamera = workspace.Cameras[1];

        var assignment = slot.AssignCommand.ExecuteAsync();
        await entered.Task;
        Assert.Equal("camera-1", slot.AssignedCameraId);

        release.SetResult();
        await assignment;
        Assert.Equal("camera-2", slot.AssignedCameraId);
        Assert.Equal("Spot 2", slot.AssignedCameraName);
    }

    [Fact]
    public async Task StatusPollingPreservesPendingSlotSelectionUntilAssignment()
    {
        var camera = Camera("camera-1", "Spot 1");
        var runtime = new FakeWorkspaceRuntimeService([camera]);
        await using var workspace = new WorkspaceViewModel(runtime);
        var slot = workspace.SelectedView.Slots[0];
        slot.SelectedCamera = workspace.Cameras[0];

        await workspace.RefreshNowAsync();

        Assert.Same(workspace.Cameras[0], slot.SelectedCamera);
        Assert.True(slot.AssignCommand.CanExecute(null));
        Assert.Null(slot.AssignedCameraId);
    }

    [Fact]
    public async Task FailedSlotAssignmentKeepsPreviousOperatorAssignment()
    {
        var cameras = new[] { Camera("camera-1", "Spot 1"), Camera("camera-2", "Spot 2") };
        var runtime = new FakeWorkspaceRuntimeService(
            cameras,
            new ViewDefinition("view-main", "Main", "camera-1"))
        {
            BindException = new InvalidOperationException("Binding rejected."),
        };
        await using var workspace = new WorkspaceViewModel(runtime);
        var slot = workspace.SelectedView.Slots[0];
        slot.SelectedCamera = workspace.Cameras[1];

        await slot.AssignCommand.ExecuteAsync();

        Assert.Equal("camera-1", slot.AssignedCameraId);
        Assert.Equal("Spot 1", slot.AssignedCameraName);
        Assert.Equal("Binding rejected.", slot.OperatorMessage);
    }

    [Fact]
    public async Task RemovingSlotBindingUpdatesLiveAssignment()
    {
        var camera = Camera("camera-1", "Spot 1");
        var runtime = new FakeWorkspaceRuntimeService(
            [camera],
            new ViewDefinition("view-main", "Main", camera.Id));
        await using var workspace = new WorkspaceViewModel(runtime);
        var slot = workspace.SelectedView.Slots[0];

        await slot.RemoveCommand.ExecuteAsync();

        Assert.Null(slot.AssignedCameraId);
        Assert.Equal("Unassigned", slot.AssignedCameraName);
        Assert.Equal(ViewSourceRuntimeState.Unbound, slot.SourceState);
        Assert.False(slot.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task LiveRuntimeSnapshotPreventsImmutableViewDefinitionFromBecomingStaleUi()
    {
        var cameras = new[] { Camera("camera-1", "Spot 1"), Camera("camera-2", "Spot 2") };
        var definition = new ViewDefinition("view-main", "Main", "camera-1");
        var runtime = new FakeWorkspaceRuntimeService(cameras, definition);
        await using var workspace = new WorkspaceViewModel(runtime);

        runtime.SetLiveBinding(0, "camera-2");
        await workspace.RefreshNowAsync();

        Assert.Equal("camera-1", definition.GetCameraId(0));
        Assert.Equal("camera-2", workspace.SelectedView.Slots[0].AssignedCameraId);
        Assert.Equal("Spot 2", workspace.SelectedView.Slots[0].AssignedCameraName);
    }

    [Fact]
    public async Task OutputEnablementUsesDefinitionRuntimeAndBusyState()
    {
        var output = Output(enabled: true);
        var runtime = new FakeWorkspaceRuntimeService(output: output);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StartOutputHandler = async () =>
        {
            entered.SetResult();
            await release.Task;
        };
        await using var workspace = new WorkspaceViewModel(runtime);
        var item = workspace.Outputs[0];

        Assert.True(item.StartCommand.CanExecute(null));
        Assert.False(item.StopCommand.CanExecute(null));
        var start = item.StartCommand.ExecuteAsync();
        await entered.Task;
        Assert.True(item.IsBusy);
        Assert.False(item.StartCommand.CanExecute(null));
        Assert.False(item.StopCommand.CanExecute(null));
        release.SetResult();
        await start;
        Assert.True(item.StopCommand.CanExecute(null));

        var disabledRuntime = new FakeWorkspaceRuntimeService(output: Output(enabled: false));
        await using var disabledWorkspace = new WorkspaceViewModel(disabledRuntime);
        Assert.False(disabledWorkspace.Outputs[0].StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task MultipleViewsCanBeAddedSelectedAndConfiguredIndependently()
    {
        var camera = Camera("camera-1", "Spot 1");
        var viewA = new ViewDefinition("view-a", "Spots A", camera.Id);
        var viewB = new ViewDefinition("view-b", "Spots B");
        var outputA = new OutputDefinition("output-a", "Output A", "ROBOCAM - A", viewA.Id);
        var runtime = new FakeWorkspaceRuntimeService(
            [camera],
            views: [viewA, viewB],
            outputs: [outputA]);
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.Preview.Attach(new PreviewHostSurface(PreviewHostPlatform.MacOSNsView, 42, 30));

        workspace.PendingSelectedView = workspace.Views[1];
        await workspace.SelectViewCommand.ExecuteAsync();
        workspace.SelectedView.Slots[0].SelectedCamera = workspace.Cameras[0];
        await workspace.SelectedView.Slots[0].AssignCommand.ExecuteAsync();

        Assert.Equal("view-b", workspace.SelectedView.Definition.Id);
        Assert.Equal("view-b", workspace.Preview.SelectedViewId);
        Assert.Equal(1, runtime.PreviewSwitchCount);
        Assert.Equal("camera-1", workspace.SelectedView.Slots[0].AssignedCameraId);
        Assert.Equal("camera-1", viewA.GetCameraId(0));
        Assert.Equal("view-a", workspace.Outputs[0].Definition.ViewId);
    }

    [Fact]
    public async Task AddViewCreatesANamedStableDefinitionWithoutReplacingExistingViews()
    {
        var runtime = new FakeWorkspaceRuntimeService();
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.NewViewName = "Spots B";

        await workspace.AddViewCommand.ExecuteAsync();

        Assert.Equal(2, workspace.Views.Count);
        Assert.Equal("Spots B", workspace.Views[1].Name);
        Assert.StartsWith("view-", workspace.Views[1].Definition.Id, StringComparison.Ordinal);
        Assert.Same(workspace.Views[1], workspace.PendingSelectedView);
        Assert.Equal("view-main", workspace.SelectedView.Definition.Id);
    }

    [Fact]
    public async Task PendingViewSelectionsSurvivePollingAndDoNotChangeOutputRouting()
    {
        var viewA = new ViewDefinition("view-a", "Spots A");
        var viewB = new ViewDefinition("view-b", "Spots B");
        var output = new OutputDefinition("output-a", "Output A", "ROBOCAM - A", viewA.Id);
        var runtime = new FakeWorkspaceRuntimeService(
            views: [viewA, viewB],
            outputs: [output]);
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.PendingSelectedView = workspace.Views[1];
        workspace.PendingOutputView = workspace.Views[1];

        await workspace.RefreshNowAsync();

        Assert.Same(workspace.Views[1], workspace.PendingSelectedView);
        Assert.Same(workspace.Views[1], workspace.PendingOutputView);
        Assert.Equal("view-a", workspace.SelectedView.Definition.Id);
        Assert.Equal("view-a", workspace.Outputs[0].Definition.ViewId);
    }

    [Fact]
    public async Task SharedCameraOutageAndRecoveryUpdatesBothViewsWithoutDuplicateOwnership()
    {
        var camera = Camera("camera-shared", "Shared Spot");
        var viewA = new ViewDefinition("view-a", "Spots A", camera.Id);
        var viewB = new ViewDefinition("view-b", "Spots B", camera.Id);
        var runtime = new FakeWorkspaceRuntimeService([camera], views: [viewA, viewB]);
        await using var workspace = new WorkspaceViewModel(runtime);
        runtime.CameraStates[camera.Id] = CameraRuntimeState.WaitingToRetry;

        await workspace.RefreshNowAsync();

        Assert.All(
            workspace.Views,
            view => Assert.Equal(ViewSourceRuntimeState.FrozenLastGood, view.Slots[0].SourceState));
        Assert.Equal(0U, workspace.ActiveRtspSessionTotal);
        Assert.Equal(0U, workspace.ActiveDecoderTotal);

        runtime.CameraStates[camera.Id] = CameraRuntimeState.Receiving;
        await workspace.RefreshNowAsync();

        Assert.All(workspace.Views, view => Assert.Equal(ViewSourceRuntimeState.Live, view.Slots[0].SourceState));
        Assert.Equal(1U, workspace.ActiveRtspSessionTotal);
        Assert.Equal(1U, workspace.ActiveDecoderTotal);
    }

    [Fact]
    public async Task MultipleOutputsStartStopAndRestartIndependently()
    {
        var view = new ViewDefinition("view-main", "Main");
        var outputA = new OutputDefinition("output-a", "Output A", "ROBOCAM - A", view.Id);
        var outputB = new OutputDefinition("output-b", "Output B", "ROBOCAM - B", view.Id);
        var runtime = new FakeWorkspaceRuntimeService(
            views: [view],
            outputs: [outputA, outputB]);
        runtime.OutputStatuses[outputA.Id] = FakeWorkspaceRuntimeService.CreateOutputStatus(OutputRuntimeState.Running);
        runtime.OutputStatuses[outputB.Id] = FakeWorkspaceRuntimeService.CreateOutputStatus(OutputRuntimeState.Running);
        await using var workspace = new WorkspaceViewModel(runtime);
        await workspace.RefreshNowAsync();

        Assert.Equal(2U, workspace.SelectedView.OutputConsumerCount);
        Assert.Equal("Outputs 2", workspace.SelectedView.OutputConsumerText);

        await workspace.Outputs[0].StopCommand.ExecuteAsync();

        Assert.Equal(OutputRuntimeState.Stopped, workspace.Outputs[0].State);
        Assert.Equal(OutputRuntimeState.Running, workspace.Outputs[1].State);
        Assert.Equal(1, runtime.StopOutputCallCounts[outputA.Id]);
        Assert.False(runtime.StopOutputCallCounts.ContainsKey(outputB.Id));

        await workspace.Outputs[0].RestartCommand.ExecuteAsync();

        Assert.Equal(OutputRuntimeState.Starting, workspace.Outputs[0].State);
        Assert.Equal(OutputRuntimeState.Running, workspace.Outputs[1].State);
        Assert.Equal(1, runtime.StartOutputCallCounts[outputA.Id]);
        Assert.False(runtime.StartOutputCallCounts.ContainsKey(outputB.Id));
    }

    [Fact]
    public async Task SlowOutputOperationDoesNotSerializeAnUnrelatedOutput()
    {
        var view = new ViewDefinition("view-main", "Main");
        var outputA = new OutputDefinition("output-a", "Output A", "ROBOCAM - A", view.Id);
        var outputB = new OutputDefinition("output-b", "Output B", "ROBOCAM - B", view.Id);
        var slowOutputEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeWorkspaceRuntimeService(
            views: [view],
            outputs: [outputA, outputB])
        {
            StartOutputHandlerById = async outputId =>
            {
                if (string.Equals(outputId, outputA.Id, StringComparison.Ordinal))
                {
                    slowOutputEntered.SetResult();
                    await releaseSlowOutput.Task;
                }
            },
        };
        await using var workspace = new WorkspaceViewModel(runtime);

        var slowStart = workspace.Outputs[0].StartCommand.ExecuteAsync();
        await slowOutputEntered.Task;
        await workspace.Outputs[1].StartCommand.ExecuteAsync();

        Assert.False(slowStart.IsCompleted);
        Assert.Equal(OutputRuntimeState.Starting, workspace.Outputs[1].State);
        Assert.Equal(1, runtime.StartOutputCallCounts[outputB.Id]);

        releaseSlowOutput.SetResult();
        await slowStart;
    }

    [Fact]
    public async Task PollingCollectionsRemainConsistentAfterViewAndOutputAdds()
    {
        var camera = Camera("camera-1", "Spot 1");
        var runtime = new FakeWorkspaceRuntimeService([camera]);
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.NewViewName = "Spots B";

        await workspace.AddViewCommand.ExecuteAsync();
        workspace.PendingSelectedView = workspace.Views[1];
        await workspace.SelectViewCommand.ExecuteAsync();
        workspace.SelectedView.Slots[0].SelectedCamera = workspace.Cameras[0];
        await workspace.SelectedView.Slots[0].AssignCommand.ExecuteAsync();
        workspace.PendingOutputView = workspace.Views[1];
        workspace.NewOutputName = "Spots B";
        workspace.NewOutputNdiSourceName = "ROBOCAM - SPOTS B";
        await workspace.AddOutputCommand.ExecuteAsync();
        await workspace.RefreshNowAsync();

        await workspace.SelectedView.Slots[0].RemoveCommand.ExecuteAsync();
        await workspace.RefreshNowAsync();

        var snapshot = await runtime.QueryStatusAsync();
        Assert.Equal(2, snapshot.Views.Count);
        Assert.Equal(2, snapshot.ViewSources.Count);
        Assert.All(snapshot.Views.Keys, viewId => Assert.True(snapshot.ViewSources.ContainsKey(viewId)));
        Assert.Single(snapshot.Outputs);
        Assert.Equal(workspace.Views[1].Definition.Id, workspace.Outputs[0].Definition.ViewId);
        Assert.Equal(4, snapshot.ViewSources[workspace.Views[1].Definition.Id].Count);
        Assert.Equal(
            ViewSourceRuntimeState.Unbound,
            snapshot.ViewSources[workspace.Views[1].Definition.Id][0].Value!.Value.State);
    }

    [Fact]
    public async Task OutputCreationRetainsItsChosenViewWhenLocalPreviewChanges()
    {
        var viewA = new ViewDefinition("view-a", "Spots A");
        var viewB = new ViewDefinition("view-b", "Spots B");
        var runtime = new FakeWorkspaceRuntimeService(views: [viewA, viewB]);
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.PendingOutputView = workspace.Views[0];
        workspace.PendingSelectedView = workspace.Views[1];
        await workspace.SelectViewCommand.ExecuteAsync();
        workspace.NewOutputName = "Backup";
        workspace.NewOutputNdiSourceName = "ROBOCAM - BACKUP";

        await workspace.AddOutputCommand.ExecuteAsync();

        Assert.Single(workspace.Outputs);
        Assert.Equal("view-a", workspace.Outputs[0].Definition.ViewId);
        Assert.Equal("view-b", workspace.SelectedView.Definition.Id);
    }

    [Fact]
    public async Task FailedPreviewSwitchPreservesCurrentSelectedViewAndOutputRoutes()
    {
        var viewA = new ViewDefinition("view-a", "Spots A");
        var viewB = new ViewDefinition("view-b", "Spots B");
        var output = new OutputDefinition("output-a", "Output A", "ROBOCAM - A", viewA.Id);
        var runtime = new FakeWorkspaceRuntimeService(
            views: [viewA, viewB],
            outputs: [output])
        {
            SwitchPreviewException = new InvalidOperationException("platform switch failed"),
        };
        runtime.OutputStatuses[output.Id] = FakeWorkspaceRuntimeService.CreateOutputStatus(
            OutputRuntimeState.Running);
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.Preview.Attach(new PreviewHostSurface(PreviewHostPlatform.MacOSNsView, 42, 30));
        await workspace.RefreshNowAsync();
        workspace.PendingSelectedView = workspace.Views[1];

        await workspace.SelectViewCommand.ExecuteAsync();

        Assert.Equal("view-a", workspace.SelectedView.Definition.Id);
        Assert.Equal("view-a", workspace.Preview.SelectedViewId);
        Assert.Equal("view-a", workspace.Outputs[0].Definition.ViewId);
        Assert.Equal(OutputRuntimeState.Running, workspace.Outputs[0].State);
        Assert.Empty(runtime.StartOutputCallCounts);
        Assert.Empty(runtime.StopOutputCallCounts);
        Assert.Equal("platform switch failed", workspace.Preview.OperatorMessage);
    }

    [Fact]
    public async Task ReceiverCountIsOmittedUntilRuntimeMarksItKnown()
    {
        var output = Output(enabled: true);
        var runtime = new FakeWorkspaceRuntimeService(output: output)
        {
            OutputStatus = FakeWorkspaceRuntimeService.CreateOutputStatus(
                OutputRuntimeState.Running,
                receiverCountKnown: false,
                receiverCount: 7),
        };
        await using var workspace = new WorkspaceViewModel(runtime);

        await workspace.RefreshNowAsync();
        Assert.False(workspace.Outputs[0].ReceiverCountKnown);

        runtime.OutputStatus = FakeWorkspaceRuntimeService.CreateOutputStatus(
            OutputRuntimeState.Running,
            receiverCountKnown: true,
            receiverCount: 2);
        await workspace.RefreshNowAsync();
        Assert.True(workspace.Outputs[0].ReceiverCountKnown);
        Assert.Equal("Receivers: 2", workspace.Outputs[0].ReceiverCountText);
    }

    [Fact]
    public async Task RuntimeErrorsBecomeConciseNonModalOperatorState()
    {
        var camera = Camera("camera-1", "Spot 1");
        var runtime = new FakeWorkspaceRuntimeService([camera])
        {
            StartCameraException = new Exception("internal implementation detail"),
        };
        await using var workspace = new WorkspaceViewModel(runtime);

        await workspace.Cameras[0].StartCommand.ExecuteAsync();

        Assert.Equal("Spot 1 start failed.", workspace.Cameras[0].OperatorMessage);
        Assert.DoesNotContain("internal implementation detail", workspace.Cameras[0].OperatorMessage);
    }

    [Theory]
    [InlineData(ViewPreviewRuntimeState.Starting, "Preview Starting", "◐")]
    [InlineData(ViewPreviewRuntimeState.Live, "Preview Live", "●")]
    [InlineData(ViewPreviewRuntimeState.WaitingForView, "Preview Waiting for View", "◐")]
    [InlineData(ViewPreviewRuntimeState.Failed, "Preview Failed", "✕")]
    public async Task PreviewRuntimeStateMapsToInlineOperatorState(
        ViewPreviewRuntimeState state,
        string expectedText,
        string expectedIcon)
    {
        var runtime = new FakeWorkspaceRuntimeService();
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.Preview.Attach(new PreviewHostSurface(PreviewHostPlatform.MacOSNsView, 42, 30));
        runtime.PreviewStatus = FakeWorkspaceRuntimeService.CreatePreviewStatus(state);

        await workspace.RefreshNowAsync();

        Assert.Equal(state, workspace.Preview.State);
        Assert.Equal(expectedText, workspace.Preview.StateText);
        Assert.Equal(expectedIcon, workspace.Preview.HealthIcon);
        Assert.Equal(
            state == ViewPreviewRuntimeState.Live ? "Preview 30.0 fps" : "Preview 0.0 fps",
            workspace.Preview.PresentationFpsText);
        Assert.Equal("Frame age 5 ms", workspace.Preview.FrameAgeText);
    }

    [Fact]
    public async Task PreviewAttachFailureIsNonModalAndDoesNotEscapeToWindowCode()
    {
        var runtime = new FakeWorkspaceRuntimeService
        {
            AttachPreviewException = new Exception("native platform detail"),
        };
        await using var workspace = new WorkspaceViewModel(runtime);

        workspace.Preview.Attach(new PreviewHostSurface(PreviewHostPlatform.WindowsHwnd, 42, 30));

        Assert.Equal(ViewPreviewRuntimeState.Failed, workspace.Preview.State);
        Assert.False(workspace.Preview.Attached);
        Assert.Equal("Preview attach failed.", workspace.Preview.OperatorMessage);
        Assert.DoesNotContain("native platform detail", workspace.Preview.OperatorMessage);
    }

    [Fact]
    public async Task DisposingWorkspaceReleasesEveryRuntimeReference()
    {
        var camera = Camera("camera-1", "Spot 1");
        var output = Output(enabled: true);
        var runtime = new FakeWorkspaceRuntimeService([camera], output: output);
        var workspace = new WorkspaceViewModel(runtime);

        await workspace.DisposeAsync();

        Assert.True(runtime.IsDisposed);
        Assert.Null(GetRuntimeField(workspace));
        Assert.Null(GetRuntimeField(workspace.Cameras[0]));
        Assert.Null(GetRuntimeField(workspace.SelectedView.Slots[0]));
        Assert.Null(GetRuntimeField(workspace.Outputs[0]));
        Assert.Null(GetRuntimeField(workspace.Preview));
    }

    [Fact]
    public async Task DisposingWorkspaceStopsItsStatusPollingBeforeRuntimeDisposal()
    {
        var runtime = new FakeWorkspaceRuntimeService();
        var workspace = new WorkspaceViewModel(
            runtime,
            pollingInterval: TimeSpan.FromMilliseconds(5));
        workspace.StartStatusPolling();
        await WaitUntilAsync(() => runtime.QueryCallCount >= 2, TimeSpan.FromSeconds(2));

        await workspace.DisposeAsync();
        var queryCountAfterDisposal = runtime.QueryCallCount;
        await Task.Delay(40);

        Assert.True(runtime.IsDisposed);
        Assert.Equal(queryCountAfterDisposal, runtime.QueryCallCount);
    }

    [Fact]
    public void UiAndApplicationSourcesContainNoRawInteropOrNativeHandles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var files = new[]
            {
                Path.Combine(repositoryRoot, "src", "RoboCamHub.App"),
                Path.Combine(repositoryRoot, "src", "RoboCamHub.Application"),
            }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => (path.EndsWith(".cs", StringComparison.Ordinal)
                            || path.EndsWith(".axaml", StringComparison.Ordinal))
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var forbidden = new[]
        {
            "LibraryImport(",
            "DllImport(",
            "SafeHandle",
            "NativeEngineHandle",
            "NativeViewHandle",
            "NativeNdiSenderHandle",
            "RoboCamHub.NativeInterop",
            "robocamhub_native",
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain(forbidden, token => source.Contains(token, StringComparison.Ordinal));
        }
    }

    private static object? GetRuntimeField(object instance)
        => instance.GetType()
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoboCamHub.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!predicate())
        {
            await Task.Delay(5, cancellation.Token);
        }
    }

    private static CameraDefinition Camera(string id, string name)
        => new(id, name, "rtsp://127.0.0.1:8554/profile2/media.smp");

    private static OutputDefinition Output(bool enabled)
        => new("output-main", "Spots A", "ROBOCAM - SPOTS A", "view-main", enabled);
}
