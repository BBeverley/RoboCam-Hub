using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public interface IWorkspaceRuntimeService : IAsyncDisposable
{
    IReadOnlyList<CameraDefinition> CameraDefinitions { get; }

    ViewDefinition ViewDefinition { get; }

    OutputDefinition? OutputDefinition { get; }

    Task AddCameraAsync(CameraDefinition definition, CancellationToken cancellationToken = default);

    Task StartCameraAsync(string cameraId, CancellationToken cancellationToken = default);

    Task StopCameraAsync(string cameraId, CancellationToken cancellationToken = default);

    Task BindCameraSourceAsync(uint slotIndex, string cameraId, CancellationToken cancellationToken = default);

    Task UnbindSourceAsync(uint slotIndex, CancellationToken cancellationToken = default);

    Task AddOutputAsync(OutputDefinition definition, CancellationToken cancellationToken = default);

    Task StartOutputAsync(string outputId, CancellationToken cancellationToken = default);

    Task StopOutputAsync(string outputId, CancellationToken cancellationToken = default);

    Task<WorkspaceRuntimeSnapshot> QueryStatusAsync(CancellationToken cancellationToken = default);
}
