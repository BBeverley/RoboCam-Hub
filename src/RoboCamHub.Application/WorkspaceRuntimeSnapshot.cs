using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

public readonly record struct RuntimeObservation<T>(T? Value, string? ErrorMessage)
    where T : struct
{
    public bool IsSuccess => Value.HasValue && ErrorMessage is null;

    public static RuntimeObservation<T> Success(T value) => new(value, null);

    public static RuntimeObservation<T> Failure(string message) => new(null, message);
}

public sealed record WorkspaceRuntimeSnapshot(
    IReadOnlyDictionary<string, RuntimeObservation<CameraRuntimeStatus>> Cameras,
    RuntimeObservation<ViewRuntimeStatus> View,
    IReadOnlyDictionary<uint, RuntimeObservation<ViewSourceRuntimeStatus>> ViewSources,
    IReadOnlyDictionary<string, RuntimeObservation<OutputRuntimeStatus>> Outputs);
