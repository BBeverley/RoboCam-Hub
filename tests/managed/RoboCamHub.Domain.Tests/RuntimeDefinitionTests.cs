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

    [Fact]
    public void LegacyTwoByTwoDefinitionMapsToStableNormalizedSceneElements()
    {
        var view = new ViewDefinition(
            "view-main",
            "Main",
            "camera-1",
            "camera-2",
            "camera-3",
            "camera-4");

        Assert.True(view.IsLegacyFourSlotLayout);
        var elements = Assert.IsAssignableFrom<IReadOnlyList<ViewSceneElementDefinition>>(view.SceneElements);
        Assert.Equal(new[] { "legacy-slot-0", "legacy-slot-1", "legacy-slot-2", "legacy-slot-3" },
            elements.Select(element => element.Id));
        Assert.Collection(
            elements.Cast<CameraElementDefinition>(),
            element => Assert.Equal((0d, 0d, 0.5d, 0.5d), (element.X, element.Y, element.Width, element.Height)),
            element => Assert.Equal((0.5d, 0d, 0.5d, 0.5d), (element.X, element.Y, element.Width, element.Height)),
            element => Assert.Equal((0d, 0.5d, 0.5d, 0.5d), (element.X, element.Y, element.Width, element.Height)),
            element => Assert.Equal((0.5d, 0.5d, 0.5d, 0.5d), (element.X, element.Y, element.Width, element.Height)));
        Assert.False(view.SceneElements is ViewSceneElementDefinition[]);
    }

    [Fact]
    public void ExplicitScenePreservesStableIdsOrderAndCameraTransforms()
    {
        var background = new CameraElementDefinition(
            "camera-background",
            "camera-1",
            -0.1,
            0,
            1.2,
            1,
            zOrder: -10,
            cropLeft: 0.1,
            rotationDegrees: 12.5,
            flipHorizontal: true,
            fitMode: CameraElementFitMode.Cover);
        var inset = new CameraElementDefinition(
            "camera-inset",
            "camera-1",
            0.7,
            0.7,
            0.25,
            0.25,
            zOrder: 20,
            visible: false);

        var view = new ViewDefinition("view-scene", "Scene", new ViewSceneElementDefinition[] { background, inset });

        Assert.False(view.IsLegacyFourSlotLayout);
        Assert.Equal(new[] { "camera-background", "camera-inset" }, view.SceneElements.Select(element => element.Id));
        Assert.Same(background, view.SceneElements[0]);
        Assert.Equal("camera-1", background.CameraId);
        Assert.Equal(CameraElementFitMode.Cover, background.FitMode);
        Assert.True(background.FlipHorizontal);
        Assert.False(inset.Visible);
    }

    [Fact]
    public void SceneValidationRejectsDuplicatesInvalidGeometryCropAndRotation()
    {
        var valid = new CameraElementDefinition("element", "camera-1", 0, 0, 1, 1);
        Assert.Throws<ArgumentException>(() => new ViewDefinition(
            "view",
            "View",
            new ViewSceneElementDefinition[] { valid, valid }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraElementDefinition(
            "zero-width", "camera-1", 0, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraElementDefinition(
            "not-finite", "camera-1", double.NaN, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraElementDefinition(
            "absurd", "camera-1", 17, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => new CameraElementDefinition(
            "overcrop", "camera-1", 0, 0, 1, 1, cropLeft: 0.5, cropRight: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraElementDefinition(
            "rotation", "camera-1", 0, 0, 1, 1, rotationDegrees: 361));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraElementDefinition(
            "fit", "camera-1", 0, 0, 1, 1, fitMode: (CameraElementFitMode)99));
    }
}
