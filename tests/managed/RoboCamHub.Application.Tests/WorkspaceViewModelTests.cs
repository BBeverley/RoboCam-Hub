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
        var slot = workspace.View.Slots[0];
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
        var slot = workspace.View.Slots[0];
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
        var slot = workspace.View.Slots[0];

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
        Assert.Equal("camera-2", workspace.View.Slots[0].AssignedCameraId);
        Assert.Equal("Spot 2", workspace.View.Slots[0].AssignedCameraName);
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
        Assert.Null(GetRuntimeField(workspace.View.Slots[0]));
        Assert.Null(GetRuntimeField(workspace.Outputs[0]));
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
