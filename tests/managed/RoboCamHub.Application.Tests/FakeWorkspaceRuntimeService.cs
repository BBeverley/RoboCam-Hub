using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application.Tests;

internal sealed class FakeWorkspaceRuntimeService : IWorkspaceRuntimeService
{
    private readonly List<CameraDefinition> _cameras;
    private readonly List<ViewDefinition> _views;
    private readonly List<OutputDefinition> _outputs;
    private readonly Dictionary<string, Dictionary<uint, string>> _bindings = new(StringComparer.Ordinal);

    public FakeWorkspaceRuntimeService(
        IEnumerable<CameraDefinition>? cameras = null,
        ViewDefinition? view = null,
        OutputDefinition? output = null,
        IEnumerable<ViewDefinition>? views = null,
        IEnumerable<OutputDefinition>? outputs = null)
    {
        _cameras = cameras?.ToList() ?? [];
        _views = views?.ToList() ?? [view ?? new ViewDefinition("view-main", "Main 2x2 View")];
        if (_views.Count == 0)
        {
            throw new ArgumentException("At least one View is required.", nameof(views));
        }
        _outputs = outputs?.ToList() ?? (output is null ? [] : [output]);
        SelectedViewId = _views[0].Id;
        foreach (var definition in _views)
        {
            var viewBindings = new Dictionary<uint, string>();
            for (var slotIndex = 0; slotIndex < ViewDefinition.SlotCount; slotIndex++)
            {
                if (definition.GetCameraId(slotIndex) is { } cameraId)
                {
                    viewBindings[(uint)slotIndex] = cameraId;
                }
            }
            _bindings.Add(definition.Id, viewBindings);
        }
    }

    public IReadOnlyList<CameraDefinition> CameraDefinitions => _cameras;

    public IReadOnlyList<ViewDefinition> ViewDefinitions => _views;

    public IReadOnlyList<OutputDefinition> OutputDefinitions => _outputs;

    public string SelectedViewId { get; private set; }

    public Dictionary<string, CameraRuntimeState> CameraStates { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, OutputRuntimeStatus> OutputStatuses { get; } = new(StringComparer.Ordinal);

    public OutputRuntimeStatus? OutputStatus
    {
        get => _outputs.Count == 0 ? null : OutputStatuses.GetValueOrDefault(_outputs[0].Id);
        set
        {
            if (_outputs.Count != 0 && value is { } status)
            {
                OutputStatuses[_outputs[0].Id] = status;
            }
        }
    }

    public Exception? BindException { get; set; }

    public Exception? UnbindException { get; set; }

    public Exception? StartCameraException { get; set; }

    public Exception? AttachPreviewException { get; set; }

    public Exception? SwitchPreviewException { get; set; }

    public Func<Task>? StartCameraHandler { get; set; }

    public Func<Task>? StartOutputHandler { get; set; }

    public Func<string, Task>? StartOutputHandlerById { get; set; }

    public Func<Task>? BindHandler { get; set; }

    public int StartCameraCallCount { get; private set; }

    public int QueryCallCount { get; private set; }

    public int PreviewSwitchCount { get; private set; }

    public Dictionary<string, int> StartOutputCallCounts { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> StopOutputCallCounts { get; } = new(StringComparer.Ordinal);

    public bool IsDisposed { get; private set; }

    public bool PreviewAttached { get; private set; }

    public ViewPreviewRuntimeStatus? PreviewStatus { get; set; }

    public Task AddCameraAsync(CameraDefinition definition, CancellationToken cancellationToken = default)
    {
        _cameras.Add(definition);
        return Task.CompletedTask;
    }

    public async Task StartCameraAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        StartCameraCallCount++;
        if (StartCameraException is not null)
        {
            throw StartCameraException;
        }

        if (StartCameraHandler is not null)
        {
            await StartCameraHandler();
        }

        CameraStates[cameraId] = CameraRuntimeState.Receiving;
    }

    public Task StopCameraAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        CameraStates[cameraId] = CameraRuntimeState.Stopped;
        return Task.CompletedTask;
    }

    public Task AddViewAsync(ViewDefinition definition, CancellationToken cancellationToken = default)
    {
        _views.Add(definition);
        _bindings.Add(definition.Id, []);
        return Task.CompletedTask;
    }

    public Task ApplyViewSceneAsync(
        string viewId,
        IReadOnlyList<ViewSceneElementDefinition> elements,
        CancellationToken cancellationToken = default)
    {
        var index = _views.FindIndex(view => view.Id == viewId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"View '{viewId}' is not part of this workspace.");
        }
        foreach (var cameraElement in elements.Cast<CameraElementDefinition>())
        {
            _ = _cameras.Single(camera => camera.Id == cameraElement.CameraId);
        }
        var current = _views[index];
        _views[index] = new ViewDefinition(current.Id, current.Name, elements);
        return Task.CompletedTask;
    }

    public async Task BindCameraSourceAsync(
        string viewId,
        uint slotIndex,
        string cameraId,
        CancellationToken cancellationToken = default)
    {
        if (BindException is not null)
        {
            throw BindException;
        }

        if (BindHandler is not null)
        {
            await BindHandler();
        }

        _bindings[viewId][slotIndex] = cameraId;
    }

    public Task UnbindSourceAsync(
        string viewId,
        uint slotIndex,
        CancellationToken cancellationToken = default)
    {
        if (UnbindException is not null)
        {
            throw UnbindException;
        }

        _bindings[viewId].Remove(slotIndex);
        return Task.CompletedTask;
    }

    public Task AddOutputAsync(OutputDefinition definition, CancellationToken cancellationToken = default)
    {
        _outputs.Add(definition);
        return Task.CompletedTask;
    }

    public async Task StartOutputAsync(string outputId, CancellationToken cancellationToken = default)
    {
        StartOutputCallCounts[outputId] = StartOutputCallCounts.GetValueOrDefault(outputId) + 1;
        if (StartOutputHandlerById is not null)
        {
            await StartOutputHandlerById(outputId);
        }
        else if (StartOutputHandler is not null)
        {
            await StartOutputHandler();
        }

        OutputStatuses[outputId] = CreateOutputStatus(OutputRuntimeState.Running);
    }

    public Task StopOutputAsync(string outputId, CancellationToken cancellationToken = default)
    {
        StopOutputCallCounts[outputId] = StopOutputCallCounts.GetValueOrDefault(outputId) + 1;
        OutputStatuses[outputId] = CreateOutputStatus(OutputRuntimeState.Stopped);
        return Task.CompletedTask;
    }

    public async Task RestartOutputAsync(string outputId, CancellationToken cancellationToken = default)
    {
        await StopOutputAsync(outputId, cancellationToken);
        await StartOutputAsync(outputId, cancellationToken);
    }

    public void AttachPreview(string viewId, PreviewHostSurface host)
    {
        if (AttachPreviewException is not null)
        {
            throw AttachPreviewException;
        }
        host.Validate();
        SelectedViewId = viewId;
        PreviewAttached = true;
        PreviewStatus = CreatePreviewStatus(ViewPreviewRuntimeState.Starting, viewId);
    }

    public void SwitchPreviewView(string viewId)
    {
        if (SwitchPreviewException is not null)
        {
            throw SwitchPreviewException;
        }
        if (!_views.Any(view => string.Equals(view.Id, viewId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"View '{viewId}' is not part of this workspace.");
        }
        SelectedViewId = viewId;
        PreviewSwitchCount++;
        if (PreviewAttached)
        {
            PreviewStatus = CreatePreviewStatus(ViewPreviewRuntimeState.Starting, viewId);
        }
    }

    public void DetachPreview()
    {
        PreviewAttached = false;
        PreviewStatus = null;
    }

    public Task<WorkspaceRuntimeSnapshot> QueryStatusAsync(CancellationToken cancellationToken = default)
    {
        QueryCallCount++;
        var cameraStatuses = _cameras.ToDictionary(
            definition => definition.Id,
            definition => RuntimeObservation<CameraRuntimeStatus>.Success(
                CreateCameraStatus(CameraStates.GetValueOrDefault(definition.Id, CameraRuntimeState.Stopped))),
            StringComparer.Ordinal);
        var viewStatuses = new Dictionary<string, RuntimeObservation<ViewRuntimeStatus>>(StringComparer.Ordinal);
        var sourceStatuses = new Dictionary<string, IReadOnlyDictionary<uint, RuntimeObservation<ViewSourceRuntimeStatus>>>(StringComparer.Ordinal);
        foreach (var view in _views)
        {
            var viewBindings = _bindings[view.Id];
            viewStatuses.Add(
                view.Id,
                RuntimeObservation<ViewRuntimeStatus>.Success(new ViewRuntimeStatus(
                    ViewRuntimeState.Running,
                    (uint)viewBindings.Count,
                    (uint)viewBindings.Count,
                    0,
                    0,
                    1920,
                    1080,
                    60,
                    60_000,
                    10,
                    5,
                    (uint)_outputs.Count(output => string.Equals(output.ViewId, view.Id, StringComparison.Ordinal)))));
            sourceStatuses.Add(
                view.Id,
                Enumerable.Range(0, ViewDefinition.SlotCount).ToDictionary(
                    slotIndex => (uint)slotIndex,
                    slotIndex =>
                    {
                        var hasBinding = viewBindings.TryGetValue((uint)slotIndex, out var cameraId);
                        var sourceState = ViewSourceRuntimeState.Unbound;
                        if (hasBinding)
                        {
                            sourceState = CameraStates.TryGetValue(cameraId!, out var cameraState)
                                ? cameraState switch
                                {
                                    CameraRuntimeState.Receiving => ViewSourceRuntimeState.Live,
                                    CameraRuntimeState.WaitingToRetry => ViewSourceRuntimeState.FrozenLastGood,
                                    _ => ViewSourceRuntimeState.WaitingForFirstFrame,
                                }
                                : ViewSourceRuntimeState.Live;
                        }
                        return RuntimeObservation<ViewSourceRuntimeStatus>.Success(new ViewSourceRuntimeStatus(
                            (uint)slotIndex,
                            sourceState,
                            hasBinding,
                            cameraId,
                            sourceState == ViewSourceRuntimeState.Live,
                            false));
                    }));
        }

        var outputs = _outputs.ToDictionary(
            definition => definition.Id,
            definition => RuntimeObservation<OutputRuntimeStatus>.Success(
                OutputStatuses.GetValueOrDefault(
                    definition.Id,
                    CreateOutputStatus(OutputRuntimeState.Stopped))),
            StringComparer.Ordinal);
        return Task.FromResult(new WorkspaceRuntimeSnapshot(
            cameraStatuses,
            viewStatuses,
            sourceStatuses,
            outputs,
            PreviewAttached
                ? RuntimeObservation<ViewPreviewRuntimeStatus>.Success(
                    PreviewStatus ?? CreatePreviewStatus(ViewPreviewRuntimeState.Live, SelectedViewId))
                : null));
    }

    public ValueTask DisposeAsync()
    {
        PreviewAttached = false;
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    public void SetLiveBinding(uint slotIndex, string? cameraId)
        => SetLiveBinding(SelectedViewId, slotIndex, cameraId);

    public void SetLiveBinding(string viewId, uint slotIndex, string? cameraId)
    {
        if (cameraId is null)
        {
            _bindings[viewId].Remove(slotIndex);
        }
        else
        {
            _bindings[viewId][slotIndex] = cameraId;
        }
    }

    public static CameraRuntimeStatus CreateCameraStatus(CameraRuntimeState state)
        => new(
            state,
            "Ok",
            state == CameraRuntimeState.Receiving ? 1U : 0U,
            state == CameraRuntimeState.Receiving ? 1U : 0U,
            state == CameraRuntimeState.Receiving,
            1280,
            720,
            10,
            9,
            5,
            0,
            0,
            0,
            0);

    public static OutputRuntimeStatus CreateOutputStatus(
        OutputRuntimeState state,
        bool receiverCountKnown = false,
        uint receiverCount = 0)
        => new(
            state,
            "Ok",
            "ROBOCAM - TEST",
            1920,
            1080,
            60,
            state == OutputRuntimeState.Running ? 60_000U : 0U,
            10,
            9,
            5,
            0,
            100,
            150,
            receiverCountKnown,
            receiverCount);

    public static ViewPreviewRuntimeStatus CreatePreviewStatus(
        ViewPreviewRuntimeState state,
        string viewId = "view-main")
        => new(
            state,
            state == ViewPreviewRuntimeState.Failed ? "InternalError" : "Ok",
            true,
            viewId,
            1920,
            1080,
            30,
            state == ViewPreviewRuntimeState.Live ? 30_000U : 0U,
            20,
            40,
            5,
            19,
            2);
}
