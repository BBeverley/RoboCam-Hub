using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application.Tests;

public sealed class ViewEditorGeometryTests
{
    [Fact]
    public void UncroppedStretchVisibleBoundsEqualDestinationBounds()
    {
        var element = Element(CameraElementFitMode.Stretch);

        var geometry = ViewEditorGeometry.Calculate(element, 1920, 1080);

        Assert.Equal(geometry.DestinationBounds, geometry.VisibleBounds);
        Assert.False(geometry.HasTransparentContainerSpace);
    }

    [Fact]
    public void HorizontallyCroppedStretchStillFillsDestinationBounds()
    {
        var element = Element(CameraElementFitMode.Stretch, cropLeft: 0.25, cropRight: 0.25);

        var geometry = ViewEditorGeometry.Calculate(element, 1920, 1080);

        Assert.Equal(geometry.DestinationBounds, geometry.VisibleBounds);
    }

    [Fact]
    public void HorizontallyCroppedContainUsesCroppedSourceAspect()
    {
        var element = Element(CameraElementFitMode.Contain, cropLeft: 0.25, cropRight: 0.25);

        var geometry = ViewEditorGeometry.Calculate(element, 1920, 1080);

        AssertRectangle(geometry.DestinationBounds, 0.1, 0.2, 0.4, 0.4);
        AssertRectangle(geometry.VisibleBounds, 0.2, 0.2, 0.2, 0.4);
        Assert.True(geometry.HasTransparentContainerSpace);
    }

    [Fact]
    public void VerticallyCroppedContainUsesCroppedSourceAspect()
    {
        var element = Element(CameraElementFitMode.Contain, cropTop: 0.25, cropBottom: 0.25);

        var geometry = ViewEditorGeometry.Calculate(element, 1920, 1080);

        AssertRectangle(geometry.VisibleBounds, 0.1, 0.3, 0.4, 0.2);
    }

    [Fact]
    public void CroppedCoverStillFillsDestinationBounds()
    {
        var element = Element(
            CameraElementFitMode.Cover,
            cropLeft: 0.2,
            cropTop: 0.1,
            cropRight: 0.15,
            cropBottom: 0.25);

        var geometry = ViewEditorGeometry.Calculate(element, 1920, 1080);

        Assert.Equal(geometry.DestinationBounds, geometry.VisibleBounds);
    }

    [Fact]
    public void FlipsChangeSamplingDirectionButNotVisibleGeometry()
    {
        var unflipped = Element(CameraElementFitMode.Contain, cropLeft: 0.25, cropRight: 0.25);
        var flipped = new CameraElementDefinition(
            unflipped.Id,
            unflipped.CameraId,
            unflipped.X,
            unflipped.Y,
            unflipped.Width,
            unflipped.Height,
            unflipped.ZOrder,
            unflipped.CropLeft,
            unflipped.CropTop,
            unflipped.CropRight,
            unflipped.CropBottom,
            unflipped.RotationDegrees,
            flipHorizontal: true,
            flipVertical: true,
            fitMode: unflipped.FitMode);

        Assert.Equal(
            ViewEditorGeometry.Calculate(unflipped, 1920, 1080),
            ViewEditorGeometry.Calculate(flipped, 1920, 1080));
    }

    [Fact]
    public void RotatedCroppedContainRotatesVisibleCornersAroundDestinationCentre()
    {
        var element = Element(
            CameraElementFitMode.Contain,
            cropLeft: 0.25,
            cropRight: 0.25,
            rotationDegrees: 90);

        var geometry = ViewEditorGeometry.Calculate(element, 1920, 1080);

        AssertPoint(geometry.VisibleCorners[0], 0.4125, 0.2222222222);
        AssertPoint(geometry.VisibleCorners[1], 0.4125, 0.5777777778);
        AssertPoint(geometry.VisibleCorners[2], 0.1875, 0.5777777778);
        AssertPoint(geometry.VisibleCorners[3], 0.1875, 0.2222222222);
        Assert.True(geometry.ContainsVisible(new EditorPoint(0.3, 0.3)));
        Assert.False(geometry.ContainsVisible(new EditorPoint(0.3, 0.1)));
    }

    [Fact]
    public async Task HitTestingRejectsTransparentContainLetterboxAndFallsThroughToVisibleLayer()
    {
        var lower = Element(CameraElementFitMode.Stretch, id: "lower", zOrder: 0);
        var top = Element(
            CameraElementFitMode.Contain,
            id: "top",
            zOrder: 1,
            cropLeft: 0.25,
            cropRight: 0.25);
        var runtime = new FakeWorkspaceRuntimeService(
            [Camera("camera-1")],
            new ViewDefinition("view-main", "Main", [lower, top]));
        await using var workspace = new WorkspaceViewModel(runtime);

        Assert.Equal("lower", workspace.SelectedView.Editor.HitTest(new EditorPoint(0.11, 0.4))?.Id);
        Assert.Equal("top", workspace.SelectedView.Editor.HitTest(new EditorPoint(0.25, 0.4))?.Id);
    }

    [Fact]
    public void ManipulationHandlesUseVisibleBoundsRatherThanContainDestination()
    {
        var element = Element(CameraElementFitMode.Contain, cropLeft: 0.25, cropRight: 0.25);

        var geometry = ViewEditorGeometry.Calculate(element, 1920, 1080);

        Assert.Equal(geometry.VisibleCorners, geometry.ManipulationCorners);
        Assert.NotEqual(geometry.DestinationCorners, geometry.ManipulationCorners);
        AssertPoint(geometry.ManipulationCorners[0], 0.2, 0.2);
        AssertPoint(geometry.ManipulationCorners[2], 0.4, 0.6);
    }

    [Fact]
    public void PartiallyOffCanvasGeometryRemainsInSceneCoordinatesAndHitTestsItsVisiblePortion()
    {
        var element = new CameraElementDefinition(
            "element",
            "camera-1",
            -0.2,
            0.2,
            0.4,
            0.4,
            cropLeft: 0.25,
            cropRight: 0.25,
            fitMode: CameraElementFitMode.Contain);

        var geometry = ViewEditorGeometry.Calculate(element, 1920, 1080);

        AssertRectangle(geometry.VisibleBounds, -0.1, 0.2, 0.2, 0.4);
        Assert.True(geometry.ContainsVisible(new EditorPoint(0.05, 0.4)));
        Assert.False(geometry.ContainsVisible(new EditorPoint(0.15, 0.4)));
    }

    [Fact]
    public async Task NegotiatedSourceGeometryRefreshDoesNotMutateOrApplyScene()
    {
        var definition = Element(CameraElementFitMode.Contain);
        var runtime = new FakeWorkspaceRuntimeService(
            [Camera("camera-1")],
            new ViewDefinition("view-main", "Main", [definition]));
        runtime.CameraStates["camera-1"] = CameraRuntimeState.Receiving;
        runtime.CameraFrameSizes["camera-1"] = (1024, 768);
        await using var workspace = new WorkspaceViewModel(runtime);

        await workspace.RefreshNowAsync();

        var element = Assert.Single(workspace.SelectedView.Editor.Elements);
        AssertRectangle(element.Geometry.VisibleBounds, 0.15, 0.2, 0.3, 0.4);
        Assert.Same(definition, workspace.SelectedView.Definition.SceneElements[0]);
        Assert.Equal(0, runtime.ApplyViewSceneCallCount);
        Assert.False(workspace.SelectedView.Editor.HasPendingTransform);
    }

    [Fact]
    public async Task CameraAddedAfterEditorCreationInvalidatesGeometryWhenFrameSizeArrives()
    {
        var runtime = new FakeWorkspaceRuntimeService(view: new ViewDefinition("view-main", "Main"));
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.NewCameraName = "Camera";
        workspace.NewCameraRtspUrl = "rtsp://127.0.0.1:8554/profile2/media.smp";
        await workspace.AddCameraCommand.ExecuteAsync();
        var camera = Assert.Single(workspace.Cameras);
        Assert.True(await workspace.SelectedView.Editor.AddCameraAsync(camera.Definition.Id));
        var element = Assert.Single(workspace.SelectedView.Editor.Elements);
        var geometryNotifications = 0;
        element.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewEditorElementViewModel.Geometry))
            {
                geometryNotifications++;
            }
        };
        runtime.CameraStates[camera.Definition.Id] = CameraRuntimeState.Receiving;
        runtime.CameraFrameSizes[camera.Definition.Id] = (1024, 768);

        await workspace.RefreshNowAsync();

        Assert.True(geometryNotifications >= 1);
        Assert.Equal(4d / 3d, element.Geometry.VisibleBounds.Width
            * ViewEditorGeometry.CanvasAspectRatio / element.Geometry.VisibleBounds.Height, 8);
    }

    [Fact]
    public async Task CroppedContainResizeTracksVisibleHandleWithoutChangingCropAndCommitsOnce()
    {
        var definition = Element(
            CameraElementFitMode.Contain,
            cropLeft: 0.25,
            cropRight: 0.25);
        var runtime = new FakeWorkspaceRuntimeService(
            [Camera("camera-1")],
            new ViewDefinition("view-main", "Main", [definition]));
        await using var workspace = new WorkspaceViewModel(runtime);
        var editor = workspace.SelectedView.Editor;
        var startingHandle = editor.Elements.Single().Geometry.ManipulationCorners[2];

        Assert.True(editor.BeginResize("element", EditorResizeCorner.BottomRight, startingHandle));
        editor.UpdateResize(new EditorPoint(0.45, 0.7), preserveAspectRatio: true, snap: false);

        var pending = Assert.IsType<CameraElementDefinition>(editor.SelectedElement!.Definition);
        Assert.True(editor.HasPendingTransform);
        Assert.Equal(0, runtime.ApplyViewSceneCallCount);
        Assert.Equal(0.25, pending.CropLeft);
        Assert.Equal(0.25, pending.CropRight);
        AssertPoint(editor.SelectedElement.Geometry.ManipulationCorners[2], 0.45, 0.7);

        Assert.True(await editor.CommitInteractionAsync());
        Assert.Equal(1, runtime.ApplyViewSceneCallCount);
        var applied = Assert.IsType<CameraElementDefinition>(Assert.Single(runtime.LastAppliedScene!));
        Assert.Equal(0.25, applied.CropLeft);
        Assert.Equal(0.25, applied.CropRight);
    }

    private static CameraElementDefinition Element(
        CameraElementFitMode fitMode,
        string id = "element",
        int zOrder = 0,
        double cropLeft = 0,
        double cropTop = 0,
        double cropRight = 0,
        double cropBottom = 0,
        double rotationDegrees = 0)
        => new(
            id,
            "camera-1",
            0.1,
            0.2,
            0.4,
            0.4,
            zOrder,
            cropLeft,
            cropTop,
            cropRight,
            cropBottom,
            rotationDegrees,
            fitMode: fitMode);

    private static CameraDefinition Camera(string id)
        => new(id, "Camera", "rtsp://127.0.0.1:8554/profile2/media.smp");

    private static void AssertRectangle(
        EditorRectangle actual,
        double x,
        double y,
        double width,
        double height)
    {
        Assert.Equal(x, actual.X, 8);
        Assert.Equal(y, actual.Y, 8);
        Assert.Equal(width, actual.Width, 8);
        Assert.Equal(height, actual.Height, 8);
    }

    private static void AssertPoint(EditorPoint actual, double x, double y)
    {
        Assert.Equal(x, actual.X, 8);
        Assert.Equal(y, actual.Y, 8);
    }
}
