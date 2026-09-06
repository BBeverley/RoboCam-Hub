using System.Diagnostics;
using RoboCamHub.Persistence;

namespace RoboCamHub.Application;

public sealed class WorkspacePersistenceCoordinator : IAsyncDisposable
{
    public static readonly TimeSpan DefaultAutosaveDelay = TimeSpan.FromSeconds(5);

    private readonly WorkspaceViewModel _workspace;
    private readonly ShowFileService _showFiles;
    private readonly RecoveryStore _recovery;
    private readonly TimeSpan _autosaveDelay;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _debounceGate = new();
    private CancellationTokenSource? _debounceCancellation;
    private Task? _debounceTask;
    private RecoveryEntry? _lastRecovery;
    private int _disposed;

    public WorkspacePersistenceCoordinator(
        WorkspaceViewModel workspace,
        ShowFileService showFiles,
        RecoveryStore recovery,
        TimeSpan? autosaveDelay = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _showFiles = showFiles ?? throw new ArgumentNullException(nameof(showFiles));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _autosaveDelay = autosaveDelay ?? DefaultAutosaveDelay;
        if (_autosaveDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(autosaveDelay));
        }
        _workspace.DurableEditCommitted += OnDurableEditCommitted;
        if (_workspace.IsDirty)
        {
            ScheduleAutosave();
        }
    }

    public int AutosaveWriteCount { get; private set; }

    public TimeSpan? LastAutosaveDuration { get; private set; }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfDisposed();
        CancelPendingAutosave();
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var show = await _workspace.CaptureShowAsync().ConfigureAwait(false);
            var recoveries = await _recovery.FindNewerAsync(cancellationToken).ConfigureAwait(false);
            var finalPath = ShowFileService.EnsureExtension(path);
            await _showFiles.SaveAsync(show, finalPath, cancellationToken).ConfigureAwait(false);
            await _workspace.MarkSavedAsync(finalPath).ConfigureAwait(false);
            foreach (var recovery in recoveries.Where(entry => entry.ShowId == show.Id))
            {
                await _recovery.DiscardAsync(recovery).ConfigureAwait(false);
            }
            _lastRecovery = null;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void ScheduleAutosave()
    {
        ThrowIfDisposed();
        CancellationTokenSource cancellation;
        lock (_debounceGate)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            _debounceCancellation = cancellation;
            _debounceTask = RunAutosaveAfterDelayAsync(cancellation.Token);
        }
    }

    internal Task WaitForPendingAutosaveAsync()
    {
        lock (_debounceGate)
        {
            return _debounceTask ?? Task.CompletedTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _workspace.DurableEditCommitted -= OnDurableEditCommitted;
        Task? pending;
        lock (_debounceGate)
        {
            _debounceCancellation?.Cancel();
            pending = _debounceTask;
        }
        if (pending is not null)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        await _writeGate.WaitAsync().ConfigureAwait(false);
        _writeGate.Release();
        _writeGate.Dispose();
        lock (_debounceGate)
        {
            _debounceCancellation?.Dispose();
            _debounceCancellation = null;
            _debounceTask = null;
        }
    }

    private async Task RunAutosaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_autosaveDelay, cancellationToken).ConfigureAwait(false);
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_workspace.IsDirty)
                {
                    return;
                }
                var stopwatch = Stopwatch.StartNew();
                var show = await _workspace.CaptureShowAsync().ConfigureAwait(false);
                _lastRecovery = await _recovery.SaveAsync(
                    show,
                    _workspace.CurrentFilePath,
                    _workspace.LastNormalSaveUtc,
                    cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                LastAutosaveDuration = stopwatch.Elapsed;
                AutosaveWriteCount++;
                // Recovery is intentionally independent from the normal dirty bit.
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await _workspace.ReportPersistenceErrorAsync("autosave", exception).ConfigureAwait(false);
        }
    }

    private void OnDurableEditCommitted(object? sender, EventArgs eventArgs) => ScheduleAutosave();

    private void CancelPendingAutosave()
    {
        lock (_debounceGate)
        {
            _debounceCancellation?.Cancel();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
