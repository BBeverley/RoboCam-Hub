using RoboCamHub.Domain;
using RoboCamHub.Persistence;

namespace RoboCamHub.Application;

public interface IWorkspaceRuntimeFactory
{
    Task<IWorkspaceRuntimeService> CreateAsync(
        ShowDefinition show,
        CancellationToken cancellationToken = default);
}

public sealed class DefaultWorkspaceRuntimeFactory : IWorkspaceRuntimeFactory
{
    public async Task<IWorkspaceRuntimeService> CreateAsync(
        ShowDefinition show,
        CancellationToken cancellationToken = default)
        => await WorkspaceRuntimeService.CreateAsync(show, cancellationToken).ConfigureAwait(false);
}

public sealed class PreparedWorkspace : IAsyncDisposable
{
    private ShowLoadResult? _loadedShow;

    internal PreparedWorkspace(WorkspaceViewModel workspace, ShowLoadResult? loadedShow)
    {
        Workspace = workspace;
        _loadedShow = loadedShow;
    }

    public WorkspaceViewModel Workspace { get; }

    public IReadOnlyList<ShowLoadWarning> Warnings => _loadedShow?.Warnings ?? [];

    public async ValueTask DisposeAsync()
    {
        await Workspace.DisposeAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref _loadedShow, null)?.Dispose();
    }
}

public sealed class WorkspaceLoadCoordinator
{
    private readonly ShowFileService _showFiles;
    private readonly RecoveryStore _recovery;
    private readonly IWorkspaceRuntimeFactory _runtimeFactory;
    private readonly IUiDispatcher _dispatcher;

    public WorkspaceLoadCoordinator(
        ShowFileService showFiles,
        RecoveryStore recovery,
        IWorkspaceRuntimeFactory runtimeFactory,
        IUiDispatcher dispatcher)
    {
        _showFiles = showFiles ?? throw new ArgumentNullException(nameof(showFiles));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<PreparedWorkspace> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _showFiles.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        return await PrepareAsync(loaded, path, recovered: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PreparedWorkspace> RecoverAsync(
        RecoveryEntry recovery,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _recovery.LoadAsync(recovery, cancellationToken).ConfigureAwait(false);
        var prepared = await PrepareAsync(
            loaded,
            recovery.SourcePath,
            recovered: true,
            cancellationToken).ConfigureAwait(false);
        await prepared.Workspace.MarkRecoveredAsync(recovery.SourcePath).ConfigureAwait(false);
        return prepared;
    }

    public async Task<PreparedWorkspace> NewAsync(CancellationToken cancellationToken = default)
    {
        var view = new ViewDefinition(WorkspaceRuntimeService.DefaultViewId, WorkspaceRuntimeService.DefaultViewName);
        var show = new ShowDefinition(
            $"show-{Guid.NewGuid():N}",
            "Untitled Show",
            [],
            [view],
            []);
        var runtime = await _runtimeFactory.CreateAsync(show, cancellationToken).ConfigureAwait(false);
        try
        {
            var workspace = new WorkspaceViewModel(
                runtime,
                _dispatcher,
                show: show);
            return new PreparedWorkspace(workspace, loadedShow: null);
        }
        catch
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<PreparedWorkspace> PrepareAsync(
        ShowLoadResult loaded,
        string? path,
        bool recovered,
        CancellationToken cancellationToken)
    {
        IWorkspaceRuntimeService? runtime = null;
        try
        {
            // Parsing, complete validation and asset materialization have already
            // succeeded. The factory now builds an entirely separate runtime graph.
            runtime = await _runtimeFactory.CreateAsync(loaded.Show, cancellationToken).ConfigureAwait(false);
            var workspace = new WorkspaceViewModel(
                runtime,
                _dispatcher,
                show: loaded.Show,
                currentFilePath: path,
                recovered: recovered);
            return new PreparedWorkspace(workspace, loaded);
        }
        catch
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            loaded.Dispose();
            throw;
        }
    }
}
