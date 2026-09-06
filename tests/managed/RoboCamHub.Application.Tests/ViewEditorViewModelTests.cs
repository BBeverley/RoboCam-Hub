using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application.Tests;

public sealed class ViewEditorViewModelTests
{
    [Fact]
    public async Task HitTestingAndSelectionChooseTopmostVisibleElementDeterministically()
    {
        var (workspace, _) = CreateWorkspace(
            Element("lower", "camera-1", zOrder: 2),
            Element("top-b", "camera-2", zOrder: 5),
            Element("top-a", "camera-3", zOrder: 5),
            Element("hidden", "camera-4", zOrder: 99, visible: false));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;

            var hit = editor.HitTest(new EditorPoint(0.3, 0.3));
            var selected = editor.SelectAt(new EditorPoint(0.3, 0.3));

            Assert.Equal("top-b", hit?.Id);
            Assert.Same(hit, selected);
            Assert.True(selected?.IsSelected);
        }
    }

    [Fact]
    public async Task RotatedElementHitTestingUsesElementLocalCoordinates()
    {
        var (workspace, _) = CreateWorkspace(
            Element("rotated", "camera-1", x: 0.4, y: 0.4, width: 0.2, height: 0.1, rotation: 90));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;

            Assert.Equal("rotated", editor.HitTest(new EditorPoint(0.5, 0.54))?.Id);
            Assert.Null(editor.HitTest(new EditorPoint(0.59, 0.45)));
        }
    }

    [Fact]
    public async Task RotatedElementHitTestingUsesSixteenByNinePixelGeometry()
    {
        var (workspace, _) = CreateWorkspace(
            Element("rotated", "camera-1", x: 0.4, y: 0.4, width: 0.2, height: 0.1, rotation: 90));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;

            // A 90-degree rotation swaps the element's pixel extents. On a
            // 16:9 canvas, the resulting normalized half-width is 0.05 / (16/9).
            Assert.Equal("rotated", editor.HitTest(new EditorPoint(0.527, 0.5))?.Id);
            Assert.Null(editor.HitTest(new EditorPoint(0.53, 0.5)));
        }
    }

    [Fact]
    public async Task NonSpatialSelectionKeepsFullyOffCanvasElementRecoverable()
    {
        var (workspace, _) = CreateWorkspace(
            Element("off-canvas", "camera-1", x: 2, y: 2, width: 0.2, height: 0.2));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;

            Assert.Null(editor.HitTest(new EditorPoint(0.5, 0.5)));
            Assert.True(editor.SelectElement("off-canvas"));
            Assert.Equal("off-canvas", editor.SelectedElement?.Id);
            Assert.Contains("off-canvas", editor.SelectedElement?.SelectionLabel, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DragIsLocalUntilReleaseAndSuccessfulCommitUpdatesAppliedScene()
    {
        var (workspace, runtime) = CreateWorkspace(Element("element", "camera-1", x: 0.1, y: 0.2));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            Assert.True(editor.BeginMove("element", new EditorPoint(0.2, 0.3)));

            editor.UpdateMove(new EditorPoint(0.4, 0.5), snap: false);

            Assert.True(editor.HasPendingTransform);
            Assert.Equal(0.3, editor.SelectedElement!.X, 8);
            Assert.Equal(0.4, editor.SelectedElement.Y, 8);
            Assert.Equal(0, runtime.ApplyViewSceneCallCount);

            Assert.True(await editor.CommitInteractionAsync());
            var applied = Assert.IsType<CameraElementDefinition>(Assert.Single(runtime.LastAppliedScene!));
            Assert.Equal(0.3, applied.X, 8);
            Assert.Equal(0.4, applied.Y, 8);
            Assert.False(editor.HasPendingTransform);
            Assert.Equal(applied, workspace.SelectedView.Definition.SceneElements[0]);
        }
    }

    [Fact]
    public async Task ClickSelectionReleaseDoesNotApplyAnUnchangedScene()
    {
        var (workspace, runtime) = CreateWorkspace(Element("element", "camera-1", x: 0.1, y: 0.2));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;

            Assert.True(editor.BeginMove("element", new EditorPoint(0.2, 0.3)));
            Assert.True(await editor.CommitInteractionAsync());

            Assert.Equal(0, runtime.ApplyViewSceneCallCount);
            Assert.False(editor.HasPendingTransform);
            Assert.Equal("element", editor.SelectedElement?.Id);
        }
    }

    [Fact]
    public async Task FailedDragCommitRestoresPreviousAppliedTransformAndShowsError()
    {
        var (workspace, runtime) = CreateWorkspace(Element("element", "camera-1", x: 0.1, y: 0.2));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.BeginMove("element", new EditorPoint(0.1, 0.2));
            editor.UpdateMove(new EditorPoint(0.7, 0.7), snap: false);
            runtime.ApplyViewSceneException = new Exception("native detail");

            Assert.False(await editor.CommitInteractionAsync());

            Assert.Equal(0.1, editor.SelectedElement!.X, 8);
            Assert.Equal(0.2, editor.SelectedElement.Y, 8);
            Assert.Equal("View scene apply failed.", editor.OperatorMessage);
            Assert.DoesNotContain("native detail", editor.OperatorMessage);
        }
    }

    [Fact]
    public async Task StatusPollingDoesNotClobberActiveDragOrPendingProperties()
    {
        var (workspace, _) = CreateWorkspace(Element("element", "camera-1", x: 0.1));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.BeginMove("element", new EditorPoint(0.1, 0.1));
            editor.UpdateMove(new EditorPoint(0.45, 0.1), snap: false);

            await workspace.RefreshNowAsync();

            Assert.True(editor.HasPendingTransform);
            Assert.Equal(0.45, editor.SelectedElement!.X, 8);
            editor.CancelInteraction();
            editor.SelectElement("element");
            var properties = editor.BeginProperties()!;
            properties.X = 0.8;

            await workspace.RefreshNowAsync();

            Assert.True(editor.HasPendingProperties);
            Assert.Same(properties, editor.ActiveProperties);
            Assert.Equal(0.8, editor.ActiveProperties!.X, 8);
            Assert.Equal(0.1, editor.SelectedElement!.X, 8);
        }
    }

    [Fact]
    public async Task ResizeEnforcesMinimumAndAspectLockWhileShiftStyleUnlockIsDeterministic()
    {
        var (workspace, _) = CreateWorkspace(
            Element("element", "camera-1", x: 0.1, y: 0.1, width: 0.4, height: 0.2));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.BeginResize("element", EditorResizeCorner.BottomRight, new EditorPoint(0.5, 0.3));
            editor.UpdateResize(new EditorPoint(0.11, 0.11), preserveAspectRatio: true, snap: false);

            Assert.True(editor.SelectedElement!.Width >= ViewEditorViewModel.MinimumElementSize);
            Assert.True(editor.SelectedElement.Height >= ViewEditorViewModel.MinimumElementSize);
            Assert.Equal(2, editor.SelectedElement.Width / editor.SelectedElement.Height, 8);

            editor.CancelInteraction();
            editor.BeginResize("element", EditorResizeCorner.BottomRight, new EditorPoint(0.5, 0.3));
            editor.UpdateResize(new EditorPoint(0.35, 0.25), preserveAspectRatio: false, snap: false);

            Assert.Equal(0.25, editor.SelectedElement!.Width, 8);
            Assert.Equal(0.15, editor.SelectedElement.Height, 8);
        }
    }

    [Fact]
    public async Task RotatedResizeUsesElementLocalPixelAxesAndKeepsOppositeCornerFixed()
    {
        var (workspace, _) = CreateWorkspace(
            Element("element", "camera-1", x: 0.4, y: 0.4, width: 0.2, height: 0.1, rotation: 90));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.BeginResize("element", EditorResizeCorner.TopLeft, new EditorPoint(0.528125, 0.2722222222));

            editor.UpdateResize(new EditorPoint(0.5, 0.45), preserveAspectRatio: true, snap: false);

            Assert.Equal(0.1, editor.SelectedElement!.Width, 8);
            Assert.Equal(0.05, editor.SelectedElement.Height, 8);
            Assert.Equal(0.4359375, editor.SelectedElement.X, 8);
            Assert.Equal(0.5138888889, editor.SelectedElement.Y, 8);
        }
    }

    [Fact]
    public async Task MoveSnappingUsesDocumentedCanvasCentreTolerance()
    {
        var (workspace, _) = CreateWorkspace(Element("element", "camera-1", x: 0.1, width: 0.2));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.BeginMove("element", new EditorPoint(0.1, 0.1));

            editor.UpdateMove(new EditorPoint(0.4005, 0.1));

            Assert.Equal(0.4, editor.SelectedElement!.X, 8);
            Assert.Equal(1d / 240d, ViewEditorViewModel.SnapTolerance, 8);
        }
    }

    [Fact]
    public async Task MoveSnappingUsesRotatedVisibleBoundsOfNeighbouringElements()
    {
        var (workspace, _) = CreateWorkspace(
            Element("moving", "camera-1", x: 0.2, y: 0.1, width: 0.1, height: 0.1),
            Element("rotated", "camera-2", x: 0.4, y: 0.4, width: 0.2, height: 0.1, rotation: 90));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.BeginMove("moving", new EditorPoint(0.2, 0.1));

            editor.UpdateMove(new EditorPoint(0.372, 0.1));

            // The moving right edge snaps to the rotated neighbour's actual
            // visible left edge (0.471875), not its unrotated X value (0.4).
            Assert.Equal(0.371875, editor.SelectedElement!.X, 8);
        }
    }

    [Fact]
    public async Task KeyboardNudgeAppliesOneDeterministicNormalizedStep()
    {
        var (workspace, runtime) = CreateWorkspace(Element("element", "camera-1", x: 0.1, y: 0.2));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.SelectElement("element");

            Assert.True(await editor.NudgeSelectedAsync(1d / 1920, -1d / 1080));

            var applied = Assert.IsType<CameraElementDefinition>(Assert.Single(runtime.LastAppliedScene!));
            Assert.Equal(0.1 + 1d / 1920, applied.X, 8);
            Assert.Equal(0.2 - 1d / 1080, applied.Y, 8);
        }
    }

    [Fact]
    public async Task RotationHandleGestureUsesGate6AClockwiseAngleAndCommitsOnce()
    {
        var (workspace, runtime) = CreateWorkspace(
            Element("element", "camera-1", x: 0.25, y: 0.25, width: 0.5, height: 0.5));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.BeginRotate("element", new EditorPoint(0.5, 0.25));

            editor.UpdateRotation(new EditorPoint(0.75, 0.5));

            Assert.Equal(90, editor.SelectedElement!.RotationDegrees, 8);
            Assert.Equal(0, runtime.ApplyViewSceneCallCount);
            Assert.True(await editor.CommitInteractionAsync());
            Assert.Equal(90, Assert.IsType<CameraElementDefinition>(runtime.LastAppliedScene!.Single()).RotationDegrees, 8);
            Assert.Equal(1, runtime.ApplyViewSceneCallCount);
        }
    }

    [Fact]
    public async Task PointerGeometryIsClampedToGate6AFiniteBounds()
    {
        var (workspace, _) = CreateWorkspace(Element("element", "camera-1"));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.BeginMove("element", new EditorPoint(0, 0));

            editor.UpdateMove(new EditorPoint(1000, -1000), snap: false);

            Assert.Equal(ViewSceneElementDefinition.MaximumNormalizedMagnitude, editor.SelectedElement!.X);
            Assert.Equal(-ViewSceneElementDefinition.MaximumNormalizedMagnitude, editor.SelectedElement.Y);
            editor.CancelInteraction();

            editor.BeginResize("element", EditorResizeCorner.BottomRight, new EditorPoint(0.5, 0.5));
            editor.UpdateResize(new EditorPoint(1000, 1000), preserveAspectRatio: false, snap: false);
            Assert.Equal(ViewSceneElementDefinition.MaximumNormalizedMagnitude, editor.SelectedElement!.Width);
            Assert.Equal(ViewSceneElementDefinition.MaximumNormalizedMagnitude, editor.SelectedElement.Height);
        }
    }

    [Fact]
    public async Task DuplicateCreatesStableUniqueElementWithoutDuplicatingCameraOwnership()
    {
        var (workspace, runtime) = CreateWorkspace(Element("element", "camera-1"));
        runtime.CameraStates["camera-1"] = CameraRuntimeState.Receiving;
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.SelectElement("element");

            Assert.True(await editor.DuplicateSelectedAsync());
            await workspace.RefreshNowAsync();

            Assert.Equal(2, editor.Elements.Count);
            Assert.Equal(2, editor.Elements.Select(element => element.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.All(editor.Elements, element => Assert.Equal("camera-1", element.CameraId));
            Assert.Equal(1U, workspace.ActiveRtspSessionTotal);
            Assert.Equal(1U, workspace.ActiveDecoderTotal);
        }
    }

    [Fact]
    public async Task DeleteRemovesOnlySelectedElement()
    {
        var (workspace, runtime) = CreateWorkspace(
            Element("one", "camera-1"),
            Element("two", "camera-2", x: 0.5));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.SelectElement("one");

            Assert.True(await editor.DeleteSelectedAsync());

            var remaining = Assert.IsType<CameraElementDefinition>(Assert.Single(runtime.LastAppliedScene!));
            Assert.Equal("two", remaining.Id);
            Assert.Null(editor.SelectedElement);
        }
    }

    [Fact]
    public async Task ReorderingChangesOverlappingHitTestDeterministically()
    {
        var (workspace, _) = CreateWorkspace(
            Element("back", "camera-1", zOrder: 0),
            Element("front", "camera-2", zOrder: 1));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            Assert.Equal("front", editor.HitTest(new EditorPoint(0.2, 0.2))?.Id);
            editor.SelectElement("back");

            Assert.True(await editor.BringForwardAsync());

            Assert.Equal("back", editor.HitTest(new EditorPoint(0.2, 0.2))?.Id);
            Assert.True(await editor.SendBackwardAsync());
            Assert.Equal("front", editor.HitTest(new EditorPoint(0.2, 0.2))?.Id);
        }
    }

    [Fact]
    public async Task ExplicitZOrderAndTransformPropertiesApplyAsOneSceneDefinition()
    {
        var (workspace, runtime) = CreateWorkspace(Element("element", "camera-1"));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.SelectElement("element");
            Assert.True(await editor.SetSelectedZOrderAsync(42));
            var properties = editor.BeginProperties()!;
            properties.X = -0.1;
            properties.Y = 0.2;
            properties.Width = 0.7;
            properties.Height = 0.4;
            properties.CropLeft = 0.1;
            properties.CropTop = 0.2;
            properties.CropRight = 0.15;
            properties.CropBottom = 0.05;
            properties.RotationDegrees = 37;
            properties.FlipHorizontal = true;
            properties.FlipVertical = true;
            properties.Visible = false;
            properties.FitMode = CameraElementFitMode.Cover;

            Assert.True(await editor.ApplyPropertiesAsync());

            var applied = Assert.IsType<CameraElementDefinition>(Assert.Single(runtime.LastAppliedScene!));
            Assert.Equal(42, applied.ZOrder);
            Assert.Equal(-0.1, applied.X);
            Assert.Equal(0.2, applied.CropTop);
            Assert.Equal(37, applied.RotationDegrees);
            Assert.True(applied.FlipHorizontal);
            Assert.True(applied.FlipVertical);
            Assert.False(applied.Visible);
            Assert.Equal(CameraElementFitMode.Cover, applied.FitMode);
            Assert.False(editor.HasPendingProperties);
        }
    }

    [Fact]
    public async Task InvalidOrFailedPropertiesNeverReplaceAppliedEditorState()
    {
        var (workspace, runtime) = CreateWorkspace(Element("element", "camera-1", x: 0.1));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.SelectElement("element");
            var invalid = editor.BeginProperties()!;
            invalid.CropLeft = 0.7;
            invalid.CropRight = 0.4;

            Assert.False(await editor.ApplyPropertiesAsync());
            Assert.Equal(0, runtime.ApplyViewSceneCallCount);
            Assert.Equal(0.1, editor.SelectedElement!.X);
            Assert.True(editor.HasPendingProperties);

            invalid.CropLeft = 0;
            invalid.CropRight = 0;
            invalid.X = 0.9;
            runtime.ApplyViewSceneException = new InvalidOperationException("apply rejected");
            Assert.False(await editor.ApplyPropertiesAsync());
            Assert.Equal(0.1, editor.SelectedElement.X);
            Assert.True(editor.HasPendingProperties);
        }
    }

    [Fact]
    public async Task AddCameraCreatesCentredDefaultElementWithStableIdentityAndTopZOrder()
    {
        var (workspace, runtime) = CreateWorkspace(Element("existing", "camera-1", zOrder: 3));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;

            Assert.True(await editor.AddCameraAsync("camera-2"));

            var added = runtime.LastAppliedScene!.OfType<CameraElementDefinition>().Single(element => element.CameraId == "camera-2");
            Assert.StartsWith("camera-element-", added.Id, StringComparison.Ordinal);
            Assert.Equal(0.275, added.X, 8);
            Assert.Equal(0.275, added.Y, 8);
            Assert.Equal(0.5, added.Width);
            Assert.Equal(0.5, added.Height);
            Assert.Equal(4, added.ZOrder);
            Assert.Equal(CameraElementFitMode.Contain, added.FitMode);
        }
    }

    [Fact]
    public async Task EditorStateIsIndependentPerViewAndSelectionDoesNotRerouteOutput()
    {
        var cameras = Cameras();
        var viewA = new ViewDefinition("view-a", "A", [Element("a", "camera-1")]);
        var viewB = new ViewDefinition("view-b", "B", [Element("b", "camera-2", x: 0.5)]);
        var output = new OutputDefinition("output", "Output", "ROBOCAM - OUTPUT", viewA.Id);
        var runtime = new FakeWorkspaceRuntimeService(cameras, views: [viewA, viewB], outputs: [output]);
        await using var workspace = new WorkspaceViewModel(runtime);
        workspace.SelectedView.Editor.SelectElement("a");
        await workspace.SelectedView.Editor.NudgeSelectedAsync(0.1, 0);
        workspace.PendingSelectedView = workspace.Views[1];

        await workspace.SelectViewCommand.ExecuteAsync();

        Assert.Equal("view-b", workspace.SelectedView.Definition.Id);
        Assert.Null(workspace.SelectedView.Editor.SelectedElement);
        Assert.Equal(0.5, workspace.SelectedView.Editor.Elements.Single().X);
        Assert.Equal("view-a", workspace.Outputs.Single().Definition.ViewId);
        workspace.PendingSelectedView = workspace.Views[0];
        await workspace.SelectViewCommand.ExecuteAsync();
        Assert.Equal(0.2, workspace.SelectedView.Editor.Elements.Single().X, 8);
    }

    [Fact]
    public async Task LocateSourceSelectsOnlyTheMatchingCameraRailItem()
    {
        var (workspace, _) = CreateWorkspace(Element("element", "camera-3"));
        await using (workspace)
        {
            workspace.LocateCamera("camera-3");

            Assert.Equal("camera-3", workspace.LocatedCamera?.Definition.Id);
            Assert.True(workspace.Cameras.Single(camera => camera.Definition.Id == "camera-3").IsLocatedInEditor);
            Assert.All(
                workspace.Cameras.Where(camera => camera.Definition.Id != "camera-3"),
                camera => Assert.False(camera.IsLocatedInEditor));

            workspace.LocateCamera("missing-camera");
            Assert.Null(workspace.LocatedCamera);
            Assert.All(workspace.Cameras, camera => Assert.False(camera.IsLocatedInEditor));
        }
    }

    [Fact]
    public async Task EditorChromeIsNotRepresentableInAtomicNativeScenePayload()
    {
        var (workspace, runtime) = CreateWorkspace(Element("element", "camera-1"));
        await using (workspace)
        {
            var editor = workspace.SelectedView.Editor;
            editor.SelectElement("element");
            editor.BeginMove("element", new EditorPoint(0, 0));
            editor.UpdateMove(new EditorPoint(0.1, 0.1), snap: false);
            await editor.CommitInteractionAsync();

            Assert.All(runtime.LastAppliedScene!, element => Assert.IsType<CameraElementDefinition>(element));
            var definitionProperties = typeof(CameraElementDefinition).GetProperties().Select(property => property.Name);
            Assert.DoesNotContain(nameof(ViewEditorElementViewModel.IsSelected), definitionProperties);
            Assert.DoesNotContain(nameof(ViewEditorViewModel.HasPendingTransform), definitionProperties);
        }
    }

    private static (WorkspaceViewModel Workspace, FakeWorkspaceRuntimeService Runtime) CreateWorkspace(
        params CameraElementDefinition[] elements)
    {
        var runtime = new FakeWorkspaceRuntimeService(
            Cameras(),
            new ViewDefinition("view-main", "Main", elements));
        return (new WorkspaceViewModel(runtime), runtime);
    }

    private static CameraDefinition[] Cameras()
        =>
        [
            Camera("camera-1", "Spot 1"),
            Camera("camera-2", "Spot 2"),
            Camera("camera-3", "Spot 3"),
            Camera("camera-4", "Spot 4"),
        ];

    private static CameraElementDefinition Element(
        string id,
        string cameraId,
        double x = 0.1,
        double y = 0.1,
        double width = 0.4,
        double height = 0.4,
        int zOrder = 0,
        double rotation = 0,
        bool visible = true)
        => new(
            id,
            cameraId,
            x,
            y,
            width,
            height,
            zOrder,
            rotationDegrees: rotation,
            visible: visible);

    private static CameraDefinition Camera(string id, string name)
        => new(id, name, "rtsp://127.0.0.1:8554/profile2/media.smp");
}
