using System.Diagnostics;

namespace RoboCamHub.Application;

public sealed class StatusPollingService : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly Func<Exception, Task>? _onError;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private bool _disposed;

    public StatusPollingService(
        Func<CancellationToken, Task> refresh,
        TimeSpan interval,
        Func<Exception, Task>? onError = null)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        _interval = interval;
        _onError = onError;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _runTask is { IsCompleted: false };
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is { IsCompleted: false })
            {
                return;
            }

            _cancellation = new CancellationTokenSource();
            _runTask = RunAsync(_cancellation.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? runTask;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            runTask = _runTask;
            cancellation = _cancellation;
            cancellation?.Cancel();
        }

        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
            {
            }
        }

        cancellation?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _refresh(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Trace.TraceError("Workspace status refresh failed: {0}", exception);
                if (_onError is not null)
                {
                    await _onError(exception).ConfigureAwait(false);
                }
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
