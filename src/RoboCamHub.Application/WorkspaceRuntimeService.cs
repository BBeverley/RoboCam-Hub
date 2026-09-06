using System.Diagnostics;
using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public sealed class WorkspaceRuntimeService : IWorkspaceRuntimeService
{
    public const string DefaultViewId = "main-view";
    public const string DefaultViewName = "Main 2x2 View";

    private sealed record OutputEntry(OutputDefinition Definition, OutputRuntime Runtime, SemaphoreSlim Gate);

    private readonly ShowRuntime _showRuntime;
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly object _definitionGate = new();
    private readonly List<CameraDefinition> _cameraDefinitions = [];
    private readonly Dictionary<string, ViewRuntime> _viewRuntimes = new(StringComparer.Ordinal);
    private readonly List<ViewDefinition> _viewDefinitions = [];
    private readonly Dictionary<string, OutputEntry> _outputs = new(StringComparer.Ordinal);
    private readonly object _previewGate = new();
    private ViewPreviewRuntime? _preview;
    private PreviewHostSurface? _previewHost;
    private string _selectedViewId;
    private int _disposed;

    private WorkspaceRuntimeService(
        ShowRuntime showRuntime,
        ViewDefinition initialView,
        ViewRuntime initialViewRuntime)
    {
        _showRuntime = showRuntime;
        _selectedViewId = initialView.Id;
        _viewDefinitions.Add(initialView);
        _viewRuntimes.Add(initialView.Id, initialViewRuntime);
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

    public IReadOnlyList<ViewDefinition> ViewDefinitions
    {
        get
        {
            lock (_definitionGate)
            {
                return [.. _viewDefinitions];
            }
        }
    }

    public IReadOnlyList<OutputDefinition> OutputDefinitions
    {
        get
        {
            lock (_definitionGate)
            {
                return [.. _outputs.Values.Select(entry => entry.Definition)];
            }
        }
    }

    public string SelectedViewId
    {
        get
        {
            lock (_definitionGate)
            {
                return _selectedViewId;
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
                    var viewRuntime = runtime.AddView(viewDefinition);
                    return new WorkspaceRuntimeService(runtime, viewDefinition, viewRuntime);
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

    public Task AddViewAsync(ViewDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return RunAsync(
            showRuntime =>
            {
                var runtime = showRuntime.AddView(definition);
                lock (_definitionGate)
                {
                    _viewRuntimes.Add(definition.Id, runtime);
                    _viewDefinitions.Add(definition);
                }
            },
            cancellationToken);
    }

    public Task BindCameraSourceAsync(
        string viewId,
        uint slotIndex,
        string cameraId,
        CancellationToken cancellationToken = default)
        => RunAsync(
            _ => GetViewRuntime(viewId).BindCameraSource(slotIndex, cameraId),
            cancellationToken);

    public Task UnbindSourceAsync(
        string viewId,
        uint slotIndex,
        CancellationToken cancellationToken = default)
        => RunAsync(
            _ => GetViewRuntime(viewId).UnbindSource(slotIndex),
            cancellationToken);

    public Task AddOutputAsync(OutputDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return RunAsync(
            showRuntime =>
            {
                var runtime = showRuntime.AddOutput(definition);
                lock (_definitionGate)
                {
                    _outputs.Add(
                        definition.Id,
                        new OutputEntry(definition, runtime, new SemaphoreSlim(1, 1)));
                }
            },
            cancellationToken);
    }

    public Task StartOutputAsync(string outputId, CancellationToken cancellationToken = default)
        => RunOutputAsync(outputId, output => output.Start(), cancellationToken);

    public Task StopOutputAsync(string outputId, CancellationToken cancellationToken = default)
        => RunOutputAsync(outputId, output => output.Stop(), cancellationToken);

    public Task RestartOutputAsync(string outputId, CancellationToken cancellationToken = default)
        => RunOutputAsync(
            outputId,
            output =>
            {
                output.Stop();
                output.Start();
            },
            cancellationToken);

    public void AttachPreview(string viewId, PreviewHostSurface host)
    {
        ThrowIfDisposed();
        host.Validate();
        var view = GetViewRuntime(viewId);
        lock (_previewGate)
        {
            ThrowIfDisposed();
            _preview?.Dispose();
            _preview = view.AttachPreview(host);
            _previewHost = host;
            SetSelectedViewId(viewId);
        }
    }

    public void SwitchPreviewView(string viewId)
    {
        ThrowIfDisposed();
        var targetView = GetViewRuntime(viewId);
        lock (_previewGate)
        {
            ThrowIfDisposed();
            if (string.Equals(SelectedViewId, viewId, StringComparison.Ordinal))
            {
                return;
            }

            if (_previewHost is not { } host)
            {
                SetSelectedViewId(viewId);
                return;
            }

            var previousViewId = SelectedViewId;
            var previousView = GetViewRuntime(previousViewId);
            _preview?.Dispose();
            _preview = null;
            try
            {
                _preview = targetView.AttachPreview(host);
                SetSelectedViewId(viewId);
            }
            catch
            {
                try
                {
                    _preview = previousView.AttachPreview(host);
                }
                catch (Exception restoreException)
                {
                    Trace.TraceError(
                        "Restoring preview for View '{0}' failed after a switch error: {1}",
                        previousViewId,
                        restoreException);
                }
                throw;
            }
        }
    }

    public void DetachPreview()
    {
        lock (_previewGate)
        {
            _preview?.Dispose();
            _preview = null;
            _previewHost = null;
        }
    }

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
                KeyValuePair<string, ViewRuntime>[] views;
                OutputEntry[] outputs;
                lock (_definitionGate)
                {
                    views = [.. _viewRuntimes];
                    outputs = [.. _outputs.Values];
                }

                var viewStatuses = views.ToDictionary(
                    entry => entry.Key,
                    entry => Observe(
                        entry.Value.GetStatus,
                        $"View '{entry.Key}' status is temporarily unavailable."),
                    StringComparer.Ordinal);
                var sourceStatuses = views.ToDictionary(
                    entry => entry.Key,
                    entry => (IReadOnlyDictionary<uint, RuntimeObservation<ViewSourceRuntimeStatus>>)
                        Enumerable.Range(0, ViewDefinition.SlotCount).ToDictionary(
                            slotIndex => (uint)slotIndex,
                            slotIndex => Observe(
                                () => entry.Value.GetSourceStatus((uint)slotIndex),
                                $"View '{entry.Key}' slot {slotIndex + 1} status is temporarily unavailable.")),
                    StringComparer.Ordinal);
                var outputStatuses = outputs.ToDictionary(
                    entry => entry.Definition.Id,
                    entry => Observe(
                        entry.Runtime.GetStatus,
                        $"Output '{entry.Definition.Id}' status is temporarily unavailable."),
                    StringComparer.Ordinal);
                RuntimeObservation<ViewPreviewRuntimeStatus>? previewStatus;
                lock (_previewGate)
                {
                    previewStatus = _preview is null
                        ? null
                        : Observe(
                            _preview.GetStatus,
                            $"Preview for View '{_preview.View.Definition.Id}' status is temporarily unavailable.");
                }

                return new WorkspaceRuntimeSnapshot(
                    cameras,
                    viewStatuses,
                    sourceStatuses,
                    outputStatuses,
                    previewStatus);
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _runtimeGate.WaitAsync().ConfigureAwait(false);
        OutputEntry[] outputs;
        lock (_definitionGate)
        {
            outputs = [.. _outputs.Values.OrderBy(entry => entry.Definition.Id, StringComparer.Ordinal)];
        }

        foreach (var output in outputs)
        {
            await output.Gate.WaitAsync().ConfigureAwait(false);
        }

        try
        {
            DetachPreview();
            await Task.Run(_showRuntime.Dispose).ConfigureAwait(false);
        }
        finally
        {
            foreach (var output in outputs.Reverse())
            {
                output.Gate.Release();
            }
            _runtimeGate.Release();
            _runtimeGate.Dispose();
        }
    }

    private async Task RunOutputAsync(
        string outputId,
        Action<OutputRuntime> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        OutputEntry entry;
        lock (_definitionGate)
        {
            entry = _outputs.TryGetValue(outputId, out var found)
                ? found
                : throw new InvalidOperationException($"Output '{outputId}' is not part of this workspace.");
        }

        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await Task.Run(() => operation(entry.Runtime)).ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private ViewRuntime GetViewRuntime(string viewId)
    {
        lock (_definitionGate)
        {
            return _viewRuntimes.TryGetValue(viewId, out var view)
                ? view
                : throw new InvalidOperationException($"View '{viewId}' is not part of this workspace.");
        }
    }

    private void SetSelectedViewId(string viewId)
    {
        lock (_definitionGate)
        {
            _selectedViewId = viewId;
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
