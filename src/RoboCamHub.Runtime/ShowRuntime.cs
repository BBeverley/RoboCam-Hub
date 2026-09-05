using RoboCamHub.Domain;
using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime;

public sealed class ShowRuntime : IDisposable
{
    private readonly INativeRuntimeEngine _nativeEngine;
    private readonly Dictionary<string, CameraRuntime> _cameras = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ViewRuntime> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OutputRuntime> _outputs = new(StringComparer.Ordinal);
    private bool _disposed;

    private ShowRuntime(INativeRuntimeEngine nativeEngine)
    {
        _nativeEngine = nativeEngine;
    }

    public bool IsDisposed => _disposed;

    public IReadOnlyList<CameraRuntime> Cameras => [.. _cameras.Values];

    public IReadOnlyList<ViewRuntime> Views => [.. _views.Values];

    public IReadOnlyList<OutputRuntime> Outputs => [.. _outputs.Values];

    public static ShowRuntime Create()
        => Create(new NativeRuntimeFactory());

    internal static ShowRuntime Create(INativeRuntimeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new ShowRuntime(factory.CreateEngine());
    }

    public CameraRuntime AddCamera(CameraDefinition definition)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(definition);
        if (_cameras.ContainsKey(definition.Id))
        {
            throw new InvalidOperationException(
                $"A camera runtime with logical ID '{definition.Id}' already exists.");
        }

        var config = new NativeCameraConfig(
            definition.Id,
            definition.RtspUrl,
            definition.ConnectTimeoutMs);
        RuntimeGuard.EnsureSuccess(
            $"Adding camera '{definition.Id}'",
            _nativeEngine.AddOrUpdateCamera(config));

        var runtime = new CameraRuntime(this, _nativeEngine, definition);
        _cameras.Add(definition.Id, runtime);
        return runtime;
    }

    public ViewRuntime AddView(ViewDefinition definition)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(definition);
        if (_views.ContainsKey(definition.Id))
        {
            throw new InvalidOperationException($"A View runtime with ID '{definition.Id}' already exists.");
        }

        for (var slotIndex = 0; slotIndex < ViewDefinition.SlotCount; slotIndex++)
        {
            var cameraId = definition.GetCameraId(slotIndex);
            if (cameraId is not null && !_cameras.ContainsKey(cameraId))
            {
                throw new RuntimeReferenceException(
                    $"View '{definition.Id}' slot {slotIndex} references missing camera '{cameraId}'.");
            }
        }

        var createResult = _nativeEngine.TryCreateView(definition.Id, out var nativeView);
        RuntimeGuard.EnsureSuccess($"Creating View '{definition.Id}'", createResult);
        if (nativeView is null)
        {
            throw new InvalidOperationException(
                $"Creating View '{definition.Id}' succeeded without returning a managed native wrapper.");
        }

        try
        {
            for (var slotIndex = 0; slotIndex < ViewDefinition.SlotCount; slotIndex++)
            {
                var cameraId = definition.GetCameraId(slotIndex);
                if (cameraId is not null)
                {
                    RuntimeGuard.EnsureSuccess(
                        $"Binding View '{definition.Id}' slot {slotIndex} to camera '{cameraId}'",
                        nativeView.BindCameraSource((uint)slotIndex, cameraId));
                }
            }

            var runtime = new ViewRuntime(this, nativeView, definition);
            _views.Add(definition.Id, runtime);
            return runtime;
        }
        catch
        {
            nativeView.Dispose();
            throw;
        }
    }

    public OutputRuntime AddOutput(OutputDefinition definition)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(definition);
        if (_outputs.ContainsKey(definition.Id))
        {
            throw new InvalidOperationException($"An Output runtime with ID '{definition.Id}' already exists.");
        }

        if (_outputs.Count != 0)
        {
            throw new InvalidOperationException("Gate 5A supports one managed Output runtime.");
        }

        if (!_views.TryGetValue(definition.ViewId, out var view))
        {
            throw new RuntimeReferenceException(
                $"Output '{definition.Id}' references missing View '{definition.ViewId}'.");
        }

        var createResult = view.NativeView.TryCreateSender(definition.NdiSourceName, out var nativeSender);
        RuntimeGuard.EnsureSuccess($"Creating Output '{definition.Id}'", createResult);
        if (nativeSender is null)
        {
            throw new InvalidOperationException(
                $"Creating Output '{definition.Id}' succeeded without returning a managed native wrapper.");
        }

        var runtime = new OutputRuntime(this, nativeSender, definition, view);
        _outputs.Add(definition.Id, runtime);
        return runtime;
    }

    public CameraRuntime GetCamera(string cameraId)
    {
        ThrowIfDisposed();
        return _cameras.TryGetValue(cameraId, out var camera)
            ? camera
            : throw new RuntimeReferenceException($"Camera '{cameraId}' is not part of this Show runtime.");
    }

    public ViewRuntime GetView(string viewId)
    {
        ThrowIfDisposed();
        return _views.TryGetValue(viewId, out var view)
            ? view
            : throw new RuntimeReferenceException($"View '{viewId}' is not part of this Show runtime.");
    }

    public OutputRuntime GetOutput(string outputId)
    {
        ThrowIfDisposed();
        return _outputs.TryGetValue(outputId, out var output)
            ? output
            : throw new RuntimeReferenceException($"Output '{outputId}' is not part of this Show runtime.");
    }

    public ShowRuntimeDiagnostics GetDiagnostics()
    {
        ThrowIfDisposed();
        RuntimeGuard.EnsureSuccess(
            "Querying Show runtime diagnostics",
            _nativeEngine.TryGetDiagnostics(out var diagnostics));
        return new ShowRuntimeDiagnostics(
            diagnostics.ConfiguredCameraCount,
            diagnostics.ActiveRtspSessionTotal,
            diagnostics.ActiveDecoderTotal,
            diagnostics.ViewCount,
            diagnostics.TotalBoundViewSourceCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var output in _outputs.Values.Reverse().ToArray())
        {
            output.DisposeOwned();
        }

        _outputs.Clear();
        foreach (var view in _views.Values.Reverse().ToArray())
        {
            view.DisposeOwned();
        }

        _views.Clear();
        foreach (var camera in _cameras.Values.Reverse().ToArray())
        {
            camera.DisposeOwned();
        }

        _cameras.Clear();
        _nativeEngine.Dispose();
        GC.SuppressFinalize(this);
    }

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal void DisposeView(ViewRuntime view)
    {
        if (_disposed)
        {
            view.DisposeOwned();
            return;
        }

        if (!_views.TryGetValue(view.Definition.Id, out var registered) || !ReferenceEquals(registered, view))
        {
            view.DisposeOwned();
            return;
        }

        foreach (var output in _outputs.Values
                     .Where(candidate => candidate.Definition.ViewId == view.Definition.Id)
                     .ToArray())
        {
            output.DisposeOwned();
            _outputs.Remove(output.Definition.Id);
        }

        view.DisposeOwned();
        _views.Remove(view.Definition.Id);
    }

    internal void DisposeOutput(OutputRuntime output)
    {
        if (!_disposed
            && _outputs.TryGetValue(output.Definition.Id, out var registered)
            && ReferenceEquals(registered, output))
        {
            _outputs.Remove(output.Definition.Id);
        }

        output.DisposeOwned();
    }
}
