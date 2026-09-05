using RoboCamHub.Domain;

namespace RoboCamHub.Domain.Tests;

public sealed class RuntimeDefinitionTests
{
    [Fact]
    public void DefinitionsKeepStableLogicalIdsSeparateFromNamesAndRuntimeState()
    {
        var camera = new CameraDefinition(
            "camera-spot-1",
            "Spot 1",
            "rtsp://10.0.0.10/profile2/media.smp",
            enabled: true);
        var view = new ViewDefinition(
            "view-main",
            "Main 2x2",
            slot0CameraId: camera.Id,
            slot2CameraId: camera.Id);
        var output = new OutputDefinition(
            "output-main",
            "Main Output",
            "ROBOCAM - MAIN",
            view.Id,
            enabled: true);

        Assert.Equal("camera-spot-1", camera.Id);
        Assert.Equal("Spot 1", camera.Name);
        Assert.True(camera.Enabled);
        Assert.Equal(new string?[] { "camera-spot-1", null, "camera-spot-1", null }, view.CameraIdsBySlot);
        Assert.Equal(view.Id, output.ViewId);
        Assert.Equal("ROBOCAM - MAIN", output.NdiSourceName);
        Assert.True(output.Enabled);
    }

    [Fact]
    public void DefinitionValidationRejectsInvalidCurrentGateConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new CameraDefinition(
            "camera-1",
            "Spot 1",
            "http://10.0.0.10/profile2/media.smp"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraDefinition(
            "camera-1",
            "Spot 1",
            "rtsp://10.0.0.10/profile2/media.smp",
            connectTimeoutMs: 99));
        Assert.Throws<ArgumentException>(() => new ViewDefinition("view-main", " "));
        Assert.Throws<ArgumentException>(() => new OutputDefinition(
            "output-main",
            "Main Output",
            " ",
            "view-main"));
    }

    [Fact]
    public void ViewDefinitionExposesExactlyFourReadOnlyLogicalAssignments()
    {
        var view = new ViewDefinition(
            "view-main",
            "Main",
            "camera-1",
            "camera-2",
            "camera-3",
            "camera-4");

        Assert.Equal(ViewDefinition.SlotCount, view.CameraIdsBySlot.Count);
        Assert.Equal("camera-4", view.GetCameraId(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.GetCameraId(4));
        Assert.IsAssignableFrom<IReadOnlyList<string?>>(view.CameraIdsBySlot);
        Assert.False(view.CameraIdsBySlot is string?[]);
    }
}
