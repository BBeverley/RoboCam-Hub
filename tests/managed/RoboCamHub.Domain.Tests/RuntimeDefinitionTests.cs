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

    [Fact]
    public void MultipleViewsAndOutputsRetainIndependentStableIdsAndReferences()
    {
        var views = new[]
        {
            new ViewDefinition("view-a", "Spots A", "camera-1"),
            new ViewDefinition("view-b", "Spots B", "camera-1"),
        };
        var outputs = new[]
        {
            new OutputDefinition("output-a", "Output A", "ROBOCAM - A", views[0].Id),
            new OutputDefinition("output-b", "Output B", "ROBOCAM - B", views[1].Id),
            new OutputDefinition("output-a-backup", "Output A Backup", "ROBOCAM - A BACKUP", views[0].Id),
        };

        Assert.Equal(new[] { "view-a", "view-b" }, views.Select(view => view.Id));
        Assert.Equal(new[] { "output-a", "output-b", "output-a-backup" }, outputs.Select(output => output.Id));
        Assert.Equal(new[] { "view-a", "view-b", "view-a" }, outputs.Select(output => output.ViewId));
        Assert.All(views, view => Assert.Equal("camera-1", view.GetCameraId(0)));
    }
}
