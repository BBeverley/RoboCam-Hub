using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application.Tests;

public sealed class Gate6EShowModeTests
{
    [Fact]
    public async Task WorkspaceAlwaysStartsInEditModeWithEditingCapabilitiesEnabled()
    {
        await using var workspace = CreateWorkspace().Workspace;

        Assert.Equal(WorkspaceMode.Edit, workspace.Mode);
        Assert.True(workspace.IsEditMode);
        Assert.False(workspace.IsShowMode);
        Assert.True(workspace.CanEditScene);
        Assert.True(workspace.CanCreateView);
        Assert.True(workspace.CanEditCameraAssignments);
        Assert.True(workspace.CanConfigureOutputs);
        Assert.True(workspace.CanOperateOutputs);
        Assert.True(workspace.CanSwitchPreviewView);
    }

    [Fact]
    public async Task EnteringShowModeCancelsPendingTransformAndPropertiesWithoutSceneMutation()
    {
        var (workspace, runtime) = CreateWorkspace();
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            Assert.True(editor.SelectElement("element-1"));
            Assert.True(editor.BeginMove("element-1", new EditorPoint(0.1, 0.1)));
            editor.UpdateMove(new EditorPoint(0.7, 0.6), snap: false);
            Assert.NotNull(editor.BeginProperties());

            await workspace.EnterShowModeCommand.ExecuteAsync();

            Assert.True(workspace.IsShowMode);
            Assert.False(editor.HasPendingTransform);
            Assert.False(editor.HasPendingProperties);
            Assert.Null(editor.SelectedElement);
            Assert.Equal(0.1, editor.Elements.Single().X, 8);
            Assert.Equal(0.1, editor.Elements.Single().Y, 8);
            Assert.Equal(0, runtime.ApplyViewSceneCallCount);
        }
    }

    [Fact]
    public async Task ShowModeRejectsEverySceneMutationEntryPointAndEditorShortcuts()
    {
        var (workspace, runtime) = CreateWorkspace();
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            await workspace.EnterShowModeCommand.ExecuteAsync();

            Assert.False(editor.SelectElement("element-1"));
            Assert.False(editor.BeginMove("element-1", new EditorPoint(0.1, 0.1)));
            Assert.Null(editor.BeginProperties());
            Assert.Null(editor.BeginVisualProperties());
            Assert.False(await editor.AddCameraAsync("camera-1"));
            Assert.False(await editor.AddTextAsync());
            Assert.False(await editor.AddRectangleAsync());
            Assert.False(await editor.AddFrameAsync());
            Assert.False(await editor.NudgeSelectedAsync(0.1, 0));
            Assert.False(await editor.DuplicateSelectedAsync());
            Assert.False(await editor.DeleteSelectedAsync());
            Assert.False(await editor.BringForwardAsync());
            Assert.False(await editor.SendBackwardAsync());
            Assert.False(await editor.SetSelectedZOrderAsync(2));
            Assert.Equal(0, runtime.ApplyViewSceneCallCount);
        }
    }

    [Fact]
    public async Task ShowModeDisablesViewCameraAndOutputConfigurationButKeepsOutputOperations()
    {
        var output = new OutputDefinition("output-1", "Main", "ROBOCAM - MAIN", "view-a");
        var runtime = Runtime(outputs: [output]);
        runtime.OutputStatuses[output.Id] = FakeWorkspaceRuntimeService.CreateOutputStatus(OutputRuntimeState.Running);
        await using var workspace = new WorkspaceViewModel(runtime);
        await workspace.RefreshNowAsync();
        workspace.SelectedView.Slots[0].SelectedCamera = workspace.Cameras[0];

        await workspace.EnterShowModeCommand.ExecuteAsync();

        Assert.False(workspace.AddCameraCommand.CanExecute(null));
        Assert.False(workspace.AddViewCommand.CanExecute(null));
        Assert.False(workspace.AddOutputCommand.CanExecute(null));
        Assert.False(await workspace.CreateViewAsync(new ViewDefinition("blocked", "Blocked")));
        Assert.False(workspace.SelectedView.Slots[0].AssignCommand.CanExecute(null));
        Assert.False(workspace.SelectedView.Slots[0].RemoveCommand.CanExecute(null));
        Assert.True(workspace.Outputs[0].StopCommand.CanExecute(null));
        Assert.True(workspace.Outputs[0].RestartCommand.CanExecute(null));

        await workspace.Outputs[0].StopCommand.ExecuteAsync();
        Assert.True(workspace.Outputs[0].StartCommand.CanExecute(null));
        await workspace.Outputs[0].StartCommand.ExecuteAsync();

        Assert.Equal(1, runtime.StopOutputCallCounts[output.Id]);
        Assert.Equal(1, runtime.StartOutputCallCounts[output.Id]);
    }

    [Fact]
    public async Task PollingAndLocalViewSelectionContinueWithoutChangingOutputRouting()
    {
        var viewA = View("view-a", "Spots A");
        var viewB = View("view-b", "Spots B");
        var outputA = new OutputDefinition("output-a", "Output A", "ROBOCAM - A", viewA.Id);
        var runtime = Runtime([viewA, viewB], [outputA]);
        runtime.CameraStates["camera-1"] = CameraRuntimeState.Receiving;
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.Preview.Attach(new PreviewHostSurface(PreviewHostPlatform.MacOSNsView, 42, 30));
        await workspace.EnterShowModeCommand.ExecuteAsync();

        await workspace.RefreshNowAsync();
        workspace.PendingSelectedView = workspace.Views[1];
        await workspace.SelectViewCommand.ExecuteAsync();
        await workspace.RefreshNowAsync();

        Assert.True(runtime.QueryCallCount >= 2);
        Assert.Equal("view-b", workspace.SelectedView.Definition.Id);
        Assert.Equal("view-b", workspace.Preview.SelectedViewId);
        Assert.Equal("view-a", workspace.Outputs[0].Definition.ViewId);
        Assert.Equal(1U, workspace.ActiveRtspSessionTotal);
        Assert.Equal(1U, workspace.ActiveDecoderTotal);
    }

    [Fact]
    public async Task FullscreenIsSessionOnlyAndDoesNotCreateViewsRestartOutputsOrChangeOwnership()
    {
        var output = new OutputDefinition("output-a", "Output A", "ROBOCAM - A", "view-a");
        var runtime = Runtime(outputs: [output]);
        runtime.CameraStates["camera-1"] = CameraRuntimeState.Receiving;
        runtime.OutputStatuses[output.Id] = FakeWorkspaceRuntimeService.CreateOutputStatus(OutputRuntimeState.Running);
        await using var workspace = new WorkspaceViewModel(runtime);
        await workspace.RefreshNowAsync();
        var viewCount = workspace.Views.Count;

        Assert.True(workspace.EnterFullscreen());

        Assert.True(workspace.IsFullscreen);
        Assert.Equal(workspace.SelectedView.Definition.Id, workspace.FullscreenViewId);
        Assert.Equal(workspace.SelectedView.Name, workspace.FullscreenViewName);
        Assert.Equal(viewCount, workspace.Views.Count);
        Assert.Equal(1U, workspace.ActiveRtspSessionTotal);
        Assert.Equal(1U, workspace.ActiveDecoderTotal);
        Assert.Empty(runtime.StartOutputCallCounts);
        Assert.Empty(runtime.StopOutputCallCounts);

        Assert.True(workspace.HandleEscape());
        Assert.False(workspace.IsFullscreen);
    }

    [Fact]
    public async Task FullscreenHostTransferReattachesOnlyTheExistingSelectedPreview()
    {
        var output = new OutputDefinition("output-a", "Output A", "ROBOCAM - A", "view-a");
        var runtime = Runtime(outputs: [output]);
        runtime.CameraStates["camera-1"] = CameraRuntimeState.Receiving;
        runtime.OutputStatuses[output.Id] = FakeWorkspaceRuntimeService.CreateOutputStatus(OutputRuntimeState.Running);
        await using var workspace = new WorkspaceViewModel(runtime);
        var normalHost = new PreviewHostSurface(PreviewHostPlatform.MacOSNsView, 42, 30);
        var fullscreenHost = new PreviewHostSurface(PreviewHostPlatform.MacOSNsView, 84, 30);
        workspace.Preview.Attach(normalHost);

        Assert.True(workspace.EnterFullscreen());
        workspace.Preview.Detach();
        workspace.Preview.Attach(fullscreenHost);
        workspace.Preview.Detach();
        workspace.Preview.Attach(normalHost);
        workspace.ExitFullscreen();

        Assert.Equal(3, runtime.PreviewAttachCount);
        Assert.Equal(2, runtime.PreviewDetachCount);
        Assert.Equal("view-a", runtime.SelectedViewId);
        Assert.Single(workspace.Views);
        Assert.Empty(runtime.StartOutputCallCounts);
        Assert.Empty(runtime.StopOutputCallCounts);
        await workspace.RefreshNowAsync();
        Assert.Equal(1U, workspace.ActiveRtspSessionTotal);
        Assert.Equal(1U, workspace.ActiveDecoderTotal);
    }

    [Fact]
    public async Task LeavingShowModeReenablesEditingAndNewWorkspaceStillDefaultsToEditMode()
    {
        var (workspace, runtime) = CreateWorkspace();
        await using (workspace)
        {
            await workspace.EnterShowModeCommand.ExecuteAsync();
            await workspace.ExitShowModeCommand.ExecuteAsync();

            Assert.True(workspace.CanEditScene);
            Assert.True(workspace.SelectedView.Editor.SelectElement("element-1"));
            Assert.True(await workspace.SelectedView.Editor.NudgeSelectedAsync(0.1, 0));
            Assert.Equal(1, runtime.ApplyViewSceneCallCount);
        }

        await using var reopened = CreateWorkspace().Workspace;
        Assert.Equal(WorkspaceMode.Edit, reopened.Mode);
    }

    [Fact]
    public async Task ShutdownFromShowModeAndFullscreenDetachesPreviewAndDisposesCleanly()
    {
        var (workspace, runtime) = CreateWorkspace();
        workspace.Preview.Attach(new PreviewHostSurface(PreviewHostPlatform.MacOSNsView, 42, 30));
        await workspace.EnterShowModeCommand.ExecuteAsync();
        workspace.EnterFullscreen();

        await workspace.DisposeAsync();

        Assert.True(runtime.IsDisposed);
        Assert.False(runtime.PreviewAttached);
        Assert.False(workspace.IsFullscreen);
        Assert.Equal(WorkspaceMode.Edit, workspace.Mode);
    }

    private static (WorkspaceViewModel Workspace, FakeWorkspaceRuntimeService Runtime) CreateWorkspace()
    {
        var runtime = Runtime();
        return (new WorkspaceViewModel(runtime), runtime);
    }

    private static FakeWorkspaceRuntimeService Runtime(
        IEnumerable<ViewDefinition>? views = null,
        IEnumerable<OutputDefinition>? outputs = null)
        => new(
            [new CameraDefinition("camera-1", "Spot 1", "rtsp://127.0.0.1/live")],
            views: views ?? [View("view-a", "Spots A")],
            outputs: outputs);

    private static ViewDefinition View(string id, string name)
        => new(
            id,
            name,
            [new CameraElementDefinition("element-1", "camera-1", 0.1, 0.1, 0.4, 0.4, 0)]);
}
