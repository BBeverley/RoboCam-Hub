using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public interface IWorkspaceRuntimeService : IAsyncDisposable
{
    IReadOnlyList<CameraDefinition> CameraDefinitions { get; }

    IReadOnlyList<ViewDefinition> ViewDefinitions { get; }

    IReadOnlyList<OutputDefinition> OutputDefinitions { get; }

    string SelectedViewId { get; }

    Task AddCameraAsync(CameraDefinition definition, CancellationToken cancellationToken = default);

    Task StartCameraAsync(string cameraId, CancellationToken cancellationToken = default);

    Task StopCameraAsync(string cameraId, CancellationToken cancellationToken = default);

    Task AddViewAsync(ViewDefinition definition, CancellationToken cancellationToken = default);

    Task ApplyViewSceneAsync(
        string viewId,
        IReadOnlyList<ViewSceneElementDefinition> elements,
        IReadOnlyList<AssetDefinition>? assets = null,
        CancellationToken cancellationToken = default);

    Task BindCameraSourceAsync(
        string viewId,
        uint slotIndex,
        string cameraId,
        CancellationToken cancellationToken = default);

    Task UnbindSourceAsync(
        string viewId,
        uint slotIndex,
        CancellationToken cancellationToken = default);

    Task AddOutputAsync(OutputDefinition definition, CancellationToken cancellationToken = default);

    Task StartOutputAsync(string outputId, CancellationToken cancellationToken = default);

    Task StopOutputAsync(string outputId, CancellationToken cancellationToken = default);

    Task RestartOutputAsync(string outputId, CancellationToken cancellationToken = default);

    void AttachPreview(string viewId, PreviewHostSurface host);

    void SwitchPreviewView(string viewId);

    void DetachPreview();

    Task<WorkspaceRuntimeSnapshot> QueryStatusAsync(CancellationToken cancellationToken = default);
}
