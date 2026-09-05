using System.Diagnostics;
using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public sealed class WorkspaceRuntimeService : IWorkspaceRuntimeService
{
    public const string DefaultViewId = "main-view";
    public const string DefaultViewName = "Main 2x2 View";

    private readonly ShowRuntime _showRuntime;
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly object _definitionGate = new();
    private readonly List<CameraDefinition> _cameraDefinitions = [];
    private OutputDefinition? _outputDefinition;
    private int _disposed;

    private WorkspaceRuntimeService(ShowRuntime showRuntime, ViewDefinition viewDefinition)
    {
        _showRuntime = showRuntime;
        ViewDefinition = viewDefinition;
    }

    public IReadOnlyList<CameraDefinition> CameraDefinitions
    {
        get
        {
            lock (_definitionGate)
            {
                return [.. _cameraDefinitions];
            }
        }
    }

    public ViewDefinition ViewDefinition { get; }

    public OutputDefinition? OutputDefinition
    {
        get
        {
            lock (_definitionGate)
            {
                return _outputDefinition;
            }
        }
    }

    public static Task<WorkspaceRuntimeService> CreateDefaultAsync(CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var runtime = ShowRuntime.Create();
                try
                {
                    var viewDefinition = new ViewDefinition(DefaultViewId, DefaultViewName);
                    runtime.AddView(viewDefinition);
                    return new WorkspaceRuntimeService(runtime, viewDefinition);
                }
                catch
                {
                    runtime.Dispose();
                    throw;
                }
            },
            cancellationToken);

    public Task AddCameraAsync(CameraDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return RunAsync(
            showRuntime =>
            {
                showRuntime.AddCamera(definition);
                lock (_definitionGate)
                {
                    _cameraDefinitions.Add(definition);
                }
            },
            cancellationToken);
    }

    public Task StartCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        => RunAsync(showRuntime => showRuntime.GetCamera(cameraId).Start(), cancellationToken);

    public Task StopCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        => RunAsync(showRuntime => showRuntime.GetCamera(cameraId).Stop(), cancellationToken);

    public Task BindCameraSourceAsync(
        uint slotIndex,
        string cameraId,
        CancellationToken cancellationToken = default)
        => RunAsync(
            showRuntime => showRuntime.GetView(ViewDefinition.Id).BindCameraSource(slotIndex, cameraId),
            cancellationToken);

    public Task UnbindSourceAsync(uint slotIndex, CancellationToken cancellationToken = default)
        => RunAsync(
            showRuntime => showRuntime.GetView(ViewDefinition.Id).UnbindSource(slotIndex),
            cancellationToken);

    public Task AddOutputAsync(OutputDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return RunAsync(
            showRuntime =>
            {
                showRuntime.AddOutput(definition);
                lock (_definitionGate)
                {
                    _outputDefinition = definition;
                }
            },
            cancellationToken);
    }

    public Task StartOutputAsync(string outputId, CancellationToken cancellationToken = default)
        => RunAsync(showRuntime => showRuntime.GetOutput(outputId).Start(), cancellationToken);

    public Task StopOutputAsync(string outputId, CancellationToken cancellationToken = default)
        => RunAsync(showRuntime => showRuntime.GetOutput(outputId).Stop(), cancellationToken);

    public Task<WorkspaceRuntimeSnapshot> QueryStatusAsync(CancellationToken cancellationToken = default)
        => RunAsync(
            showRuntime =>
            {
                var cameras = showRuntime.Cameras.ToDictionary(
                    camera => camera.Definition.Id,
                    camera => Observe(
                        camera.GetStatus,
                        $"Camera '{camera.Definition.Id}' status is temporarily unavailable."),
                    StringComparer.Ordinal);
                var view = showRuntime.GetView(ViewDefinition.Id);
                var viewStatus = Observe(
                    view.GetStatus,
                    $"View '{ViewDefinition.Id}' status is temporarily unavailable.");
                var sourceStatuses = Enumerable.Range(0, ViewDefinition.SlotCount).ToDictionary(
                    slotIndex => (uint)slotIndex,
                    slotIndex => Observe(
                        () => view.GetSourceStatus((uint)slotIndex),
                        $"View slot {slotIndex + 1} status is temporarily unavailable."));
                var outputs = showRuntime.Outputs.ToDictionary(
                    output => output.Definition.Id,
                    output => Observe(
                        output.GetStatus,
                        $"Output '{output.Definition.Id}' status is temporarily unavailable."),
                    StringComparer.Ordinal);
                return new WorkspaceRuntimeSnapshot(cameras, viewStatus, sourceStatuses, outputs);
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _runtimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(_showRuntime.Dispose).ConfigureAwait(false);
        }
        finally
        {
            _runtimeGate.Release();
            _runtimeGate.Dispose();
        }
    }

    private async Task RunAsync(Action<ShowRuntime> operation, CancellationToken cancellationToken)
    {
        _ = await RunAsync(
            showRuntime =>
            {
                operation(showRuntime);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RunAsync<T>(Func<ShowRuntime, T> operation, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _runtimeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await Task.Run(() => operation(_showRuntime)).ConfigureAwait(false);
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    private static RuntimeObservation<T> Observe<T>(Func<T> query, string operatorMessage)
        where T : struct
    {
        try
        {
            return RuntimeObservation<T>.Success(query());
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} {1}", operatorMessage, exception);
            return RuntimeObservation<T>.Failure(operatorMessage);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
