using RoboCamHub.Application;
using RoboCamHub.Domain;
using RoboCamHub.Persistence;

namespace RoboCamHub.Application.Tests;

public sealed class Gate6FPersistenceWorkflowTests
{
    [Fact]
    public async Task DirtyStateChangesOnlyAfterDurableConfigurationMutations()
    {
        var runtime = new FakeWorkspaceRuntimeService();
        await using var workspace = new WorkspaceViewModel(runtime);

        workspace.EnterFullscreen();
        workspace.ExitFullscreen();
        await workspace.EnterShowModeCommand.ExecuteAsync();
        await workspace.ExitShowModeCommand.ExecuteAsync();
        await workspace.RefreshNowAsync();
        Assert.False(workspace.IsDirty);

        workspace.NewCameraName = "Spot 1";
        workspace.NewCameraRtspUrl = "rtsp://10.0.0.1/stream";
        await workspace.AddCameraCommand.ExecuteAsync();
        Assert.True(workspace.IsDirty);
        Assert.EndsWith(" *", workspace.ShowFileDisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulSaveClearsDirtyWhileAutosaveDoesNot()
    {
        using var files = new TempDirectory();
        var runtime = new FakeWorkspaceRuntimeService();
        await using var workspace = new WorkspaceViewModel(runtime);
        var showFiles = new ShowFileService(Path.Combine(files.Path, "cache"));
        var recovery = new RecoveryStore(showFiles, Path.Combine(files.Path, "recovery"));
        await using var persistence = new WorkspacePersistenceCoordinator(
            workspace, showFiles, recovery, TimeSpan.FromMilliseconds(30));

        workspace.NewViewName = "Second";
        await workspace.AddViewCommand.ExecuteAsync();
        await persistence.WaitForPendingAutosaveAsync();

        Assert.True(workspace.IsDirty);
        Assert.Equal(1, persistence.AutosaveWriteCount);
        Assert.Single(await recovery.FindNewerAsync());

        var path = Path.Combine(files.Path, "saved");
        await persistence.SaveAsync(path);

        Assert.False(workspace.IsDirty);
        Assert.Equal(Path.Combine(files.Path, "saved.rchshow"), workspace.CurrentFilePath);
        Assert.Empty(await recovery.FindNewerAsync());
    }

    [Fact]
    public async Task AutosaveDebouncesRepeatedDurableEditsAndSerializesOneWrite()
    {
        using var files = new TempDirectory();
        var runtime = new FakeWorkspaceRuntimeService();
        await using var workspace = new WorkspaceViewModel(runtime);
        var showFiles = new ShowFileService(Path.Combine(files.Path, "cache"));
        var recovery = new RecoveryStore(showFiles, Path.Combine(files.Path, "recovery"));
        await using var persistence = new WorkspacePersistenceCoordinator(
            workspace, showFiles, recovery, TimeSpan.FromMilliseconds(80));

        workspace.NewCameraName = "One";
        workspace.NewCameraRtspUrl = "rtsp://10.0.0.1/stream";
        await workspace.AddCameraCommand.ExecuteAsync();
        workspace.NewCameraName = "Two";
        workspace.NewCameraRtspUrl = "rtsp://10.0.0.2/stream";
        await workspace.AddCameraCommand.ExecuteAsync();
        workspace.NewViewName = "View B";
        await workspace.AddViewCommand.ExecuteAsync();
        await persistence.WaitForPendingAutosaveAsync();

        Assert.Equal(1, persistence.AutosaveWriteCount);
        var found = Assert.Single(await recovery.FindNewerAsync());
        using var loaded = await recovery.LoadAsync(found);
        Assert.Equal(2, loaded.Show.Cameras.Count);
        Assert.Equal(2, loaded.Show.Views.Count);
    }

    [Fact]
    public async Task RecoveryOpensEditModeWithoutFullscreenAndRemainsDirty()
    {
        using var files = new TempDirectory();
        var showFiles = new ShowFileService(Path.Combine(files.Path, "cache"));
        var recovery = new RecoveryStore(showFiles, Path.Combine(files.Path, "recovery"));
        var show = new ShowDefinition("show", "Recovered", [], [new ViewDefinition("view", "View")], []);
        var entry = await recovery.SaveAsync(show, null, DateTimeOffset.MinValue);
        var factory = new RecordingWorkspaceRuntimeFactory();
        var loader = new WorkspaceLoadCoordinator(showFiles, recovery, factory, new ImmediateDispatcher());

        await using var prepared = await loader.RecoverAsync(entry);

        Assert.True(prepared.Workspace.IsEditMode);
        Assert.False(prepared.Workspace.IsFullscreen);
        Assert.True(prepared.Workspace.IsDirty);
        Assert.Equal("show", factory.LastShow!.Id);
    }

    [Fact]
    public async Task LoadFailureNeverReplacesOrDisposesCurrentWorkspace()
    {
        using var files = new TempDirectory();
        var currentRuntime = new FakeWorkspaceRuntimeService();
        await using var current = new WorkspaceViewModel(currentRuntime);
        var showFiles = new ShowFileService(Path.Combine(files.Path, "cache"));
        var recovery = new RecoveryStore(showFiles, Path.Combine(files.Path, "recovery"));
        var factory = new RecordingWorkspaceRuntimeFactory();
        var loader = new WorkspaceLoadCoordinator(
            showFiles, recovery, factory, new ImmediateDispatcher());
        var corruptPath = Path.Combine(files.Path, "corrupt.rchshow");
        await File.WriteAllTextAsync(corruptPath, "not a show");

        await Assert.ThrowsAsync<ShowFileException>(() => loader.OpenAsync(corruptPath));

        Assert.False(currentRuntime.IsDisposed);
        Assert.Equal("view-main", current.SelectedView.Definition.Id);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task ValidatedCompleteShowIsPassedToReplacementFactoryOnce()
    {
        using var files = new TempDirectory();
        var showFiles = new ShowFileService(Path.Combine(files.Path, "cache"));
        var recovery = new RecoveryStore(showFiles, Path.Combine(files.Path, "recovery"));
        var path = Path.Combine(files.Path, "show.rchshow");
        var camera = new CameraDefinition("camera", "Spot", "rtsp://10.0.0.1/stream");
        var view = new ViewDefinition("view", "View", "camera");
        var output = new OutputDefinition("output", "Output", "ROBOCAM - TEST", "view");
        await showFiles.SaveAsync(new ShowDefinition("show", "Show", [camera], [view], [output]), path);
        var factory = new RecordingWorkspaceRuntimeFactory();
        var loader = new WorkspaceLoadCoordinator(showFiles, recovery, factory, new ImmediateDispatcher());

        await using var prepared = await loader.OpenAsync(path);

        Assert.Equal(1, factory.CreateCount);
        Assert.Equal((1, 1, 1), (factory.LastShow!.Cameras.Count, factory.LastShow.Views.Count, factory.LastShow.Outputs.Count));
        Assert.False(prepared.Workspace.IsDirty);
        var candidateRuntime = Assert.IsType<FakeWorkspaceRuntimeService>(factory.LastRuntime);
        Assert.Equal(0, candidateRuntime.StartConfiguredCallCount);
        await prepared.Workspace.StartConfiguredRuntimeAsync();
        Assert.Equal(1, candidateRuntime.StartConfiguredCallCount);
    }

    private sealed class RecordingWorkspaceRuntimeFactory : IWorkspaceRuntimeFactory
    {
        public int CreateCount { get; private set; }
        public ShowDefinition? LastShow { get; private set; }
        public IWorkspaceRuntimeService? LastRuntime { get; private set; }

        public Task<IWorkspaceRuntimeService> CreateAsync(
            ShowDefinition show,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            LastShow = show;
            LastRuntime = new FakeWorkspaceRuntimeService(
                show.Cameras,
                views: show.Views,
                outputs: show.Outputs);
            return Task.FromResult(LastRuntime);
        }
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rch-g6f-app-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
