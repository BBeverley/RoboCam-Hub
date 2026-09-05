using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application.Tests;

internal sealed class FakeWorkspaceRuntimeService : IWorkspaceRuntimeService
{
    private readonly List<CameraDefinition> _cameras;
    private readonly Dictionary<uint, string> _bindings = [];

    public FakeWorkspaceRuntimeService(
        IEnumerable<CameraDefinition>? cameras = null,
        ViewDefinition? view = null,
        OutputDefinition? output = null)
    {
        _cameras = cameras?.ToList() ?? [];
        ViewDefinition = view ?? new ViewDefinition("view-main", "Main 2x2 View");
        OutputDefinition = output;
        for (var slotIndex = 0; slotIndex < ViewDefinition.SlotCount; slotIndex++)
        {
            if (ViewDefinition.GetCameraId(slotIndex) is { } cameraId)
            {
                _bindings[(uint)slotIndex] = cameraId;
            }
        }
    }

    public IReadOnlyList<CameraDefinition> CameraDefinitions => _cameras;

    public ViewDefinition ViewDefinition { get; }

    public OutputDefinition? OutputDefinition { get; private set; }

    public Dictionary<string, CameraRuntimeState> CameraStates { get; } = new(StringComparer.Ordinal);

    public OutputRuntimeStatus? OutputStatus { get; set; }

    public Exception? BindException { get; set; }

    public Exception? UnbindException { get; set; }

    public Exception? StartCameraException { get; set; }

    public Func<Task>? StartCameraHandler { get; set; }

    public Func<Task>? StartOutputHandler { get; set; }

    public Func<Task>? BindHandler { get; set; }

    public int StartCameraCallCount { get; private set; }

    public int QueryCallCount { get; private set; }

    public bool IsDisposed { get; private set; }

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

    public async Task BindCameraSourceAsync(
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

        _bindings[slotIndex] = cameraId;
    }

    public Task UnbindSourceAsync(uint slotIndex, CancellationToken cancellationToken = default)
    {
        if (UnbindException is not null)
        {
            throw UnbindException;
        }

        _bindings.Remove(slotIndex);
        return Task.CompletedTask;
    }

    public Task AddOutputAsync(OutputDefinition definition, CancellationToken cancellationToken = default)
    {
        OutputDefinition = definition;
        return Task.CompletedTask;
    }

    public async Task StartOutputAsync(string outputId, CancellationToken cancellationToken = default)
    {
        if (StartOutputHandler is not null)
        {
            await StartOutputHandler();
        }

        OutputStatus = CreateOutputStatus(OutputRuntimeState.Running);
    }

    public Task StopOutputAsync(string outputId, CancellationToken cancellationToken = default)
    {
        OutputStatus = CreateOutputStatus(OutputRuntimeState.Stopped);
        return Task.CompletedTask;
    }

    public Task<WorkspaceRuntimeSnapshot> QueryStatusAsync(CancellationToken cancellationToken = default)
    {
        QueryCallCount++;
        var cameraStatuses = _cameras.ToDictionary(
            definition => definition.Id,
            definition => RuntimeObservation<CameraRuntimeStatus>.Success(
                CreateCameraStatus(CameraStates.GetValueOrDefault(definition.Id, CameraRuntimeState.Stopped))),
            StringComparer.Ordinal);
        var sourceStatuses = Enumerable.Range(0, ViewDefinition.SlotCount).ToDictionary(
            slotIndex => (uint)slotIndex,
            slotIndex =>
            {
                var hasBinding = _bindings.TryGetValue((uint)slotIndex, out var cameraId);
                return RuntimeObservation<ViewSourceRuntimeStatus>.Success(new ViewSourceRuntimeStatus(
                    (uint)slotIndex,
                    hasBinding ? ViewSourceRuntimeState.Live : ViewSourceRuntimeState.Unbound,
                    hasBinding,
                    cameraId,
                    hasBinding,
                    false));
            });
        var outputs = OutputDefinition is null
            ? new Dictionary<string, RuntimeObservation<OutputRuntimeStatus>>(StringComparer.Ordinal)
            : new Dictionary<string, RuntimeObservation<OutputRuntimeStatus>>(StringComparer.Ordinal)
            {
                [OutputDefinition.Id] = RuntimeObservation<OutputRuntimeStatus>.Success(
                    OutputStatus ?? CreateOutputStatus(OutputRuntimeState.Stopped)),
            };
        return Task.FromResult(new WorkspaceRuntimeSnapshot(
            cameraStatuses,
            RuntimeObservation<ViewRuntimeStatus>.Success(new ViewRuntimeStatus(
                ViewRuntimeState.Running,
                (uint)_bindings.Count,
                (uint)_bindings.Count,
                0,
                0,
                1920,
                1080,
                60,
                60_000,
                10,
                5,
                OutputDefinition is null ? 0U : 1U)),
            sourceStatuses,
            outputs));
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    public void SetLiveBinding(uint slotIndex, string? cameraId)
    {
        if (cameraId is null)
        {
            _bindings.Remove(slotIndex);
        }
        else
        {
            _bindings[slotIndex] = cameraId;
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
}
