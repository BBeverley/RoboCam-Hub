using RoboCamHub.Domain;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application.Tests;

public sealed class ViewTemplateFactoryTests
{
    private readonly ViewTemplateFactory _factory = new();

    [Fact]
    public void BuiltInTemplatesHaveStableSlotIdsAndValidOnCanvasGeometry()
    {
        var firstRead = BuiltInViewTemplates.All;
        var secondRead = BuiltInViewTemplates.All;

        Assert.Same(firstRead, secondRead);
        Assert.Equal(
            new[]
            {
                "one-up",
                "two-up-horizontal",
                "two-up-vertical",
                "three-up",
                "four-up",
                "eight-up",
                "picture-in-picture",
            },
            firstRead.Select(template => template.Id));
        Assert.All(firstRead, template =>
        {
            Assert.Equal(
                template.Slots.Count,
                template.Slots.Select(slot => slot.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(
                Enumerable.Range(1, template.Slots.Count).Select(index => $"slot-{index}"),
                template.Slots.Select(slot => slot.Id));
            Assert.All(template.Slots, slot =>
            {
                Assert.True(slot.X >= 0);
                Assert.True(slot.Y >= 0);
                Assert.True(slot.Width > 0);
                Assert.True(slot.Height > 0);
                Assert.True(slot.X + slot.Width <= 1);
                Assert.True(slot.Y + slot.Height <= 1);
            });
        });
    }

    [Fact]
    public void BlankViewIsAnEmptyEditableFreeformScene()
    {
        var view = _factory.CreateBlank("Blank");

        Assert.StartsWith("view-", view.Id, StringComparison.Ordinal);
        Assert.Empty(view.SceneElements);
        Assert.False(view.IsLegacyFourSlotLayout);
    }

    [Fact]
    public void OneUpGeometryFillsCanvas()
        => AssertGeometry("one-up", [(0d, 0d, 1d, 1d)]);

    [Fact]
    public void TwoUpHorizontalGeometryHasNoGap()
        => AssertGeometry(
            "two-up-horizontal",
            [(0d, 0d, 0.5d, 1d), (0.5d, 0d, 0.5d, 1d)]);

    [Fact]
    public void TwoUpVerticalGeometryHasNoGap()
        => AssertGeometry(
            "two-up-vertical",
            [(0d, 0d, 1d, 0.5d), (0d, 0.5d, 1d, 0.5d)]);

    [Fact]
    public void FourUpGeometryIsExactTwoByTwoGrid()
        => AssertGeometry(
            "four-up",
            [
                (0d, 0d, 0.5d, 0.5d),
                (0.5d, 0d, 0.5d, 0.5d),
                (0d, 0.5d, 0.5d, 0.5d),
                (0.5d, 0.5d, 0.5d, 0.5d),
            ]);

    [Theory]
    [InlineData("three-up", 3)]
    [InlineData("eight-up", 8)]
    public void FractionalGridTemplatesCoverCanvasWithoutAccumulatedGaps(
        string templateId,
        int expectedSlotCount)
    {
        var template = Template(templateId);

        Assert.Equal(expectedSlotCount, template.Slots.Count);
        var totalArea = template.Slots.Sum(slot => slot.Width * slot.Height);
        Assert.Equal(1, totalArea, 12);
        foreach (var row in template.Slots.GroupBy(slot => slot.Y))
        {
            var ordered = row.OrderBy(slot => slot.X).ToArray();
            Assert.Equal(0, ordered[0].X, 12);
            Assert.Equal(1, ordered[^1].X + ordered[^1].Width, 12);
            for (var index = 1; index < ordered.Length; index++)
            {
                Assert.Equal(ordered[index].X, ordered[index - 1].X + ordered[index - 1].Width, 12);
            }
        }
    }

    [Fact]
    public void PictureInPictureHasIntentionalOverlapAndStableZOrder()
    {
        var template = Template("picture-in-picture");
        var main = template.Slots[0];
        var inset = template.Slots[1];

        Assert.Equal((0d, 0d, 1d, 1d, 0), (main.X, main.Y, main.Width, main.Height, main.ZOrder));
        Assert.Equal((0.67d, 0.67d, 0.3d, 0.3d, 1), (inset.X, inset.Y, inset.Width, inset.Height, inset.ZOrder));
        Assert.True(inset.X < main.X + main.Width && inset.Y < main.Y + main.Height);
        Assert.True(inset.ZOrder > main.ZOrder);
    }

    [Fact]
    public void PartialAssignmentOmitsEmptySlotsAndElementIdsAreFreshFromSlotIds()
    {
        var template = Template("four-up");
        var assignments = new Dictionary<string, string?>
        {
            [template.Slots[0].Id] = "camera-1",
            [template.Slots[1].Id] = null,
            [template.Slots[2].Id] = "camera-2",
        };

        var first = _factory.Instantiate(template, "First", assignments);
        var second = _factory.Instantiate(template, "Second", assignments);

        var firstElements = first.SceneElements.Cast<CameraElementDefinition>().ToArray();
        var secondElements = second.SceneElements.Cast<CameraElementDefinition>().ToArray();
        Assert.Equal(new[] { "camera-1", "camera-2" }, firstElements.Select(element => element.CameraId));
        Assert.All(firstElements, element => Assert.StartsWith("camera-element-", element.Id, StringComparison.Ordinal));
        Assert.Empty(firstElements.Select(element => element.Id).Intersect(template.Slots.Select(slot => slot.Id)));
        Assert.Empty(firstElements.Select(element => element.Id).Intersect(secondElements.Select(element => element.Id)));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void InstantiationPreservesTemplateSlotTransformDefaults()
    {
        var slot = new ViewTemplateSlotDefinition(
            "slot-custom",
            -0.1,
            0.2,
            0.7,
            0.6,
            zOrder: 14,
            cropLeft: 0.1,
            cropTop: 0.05,
            cropRight: 0.2,
            cropBottom: 0.15,
            rotationDegrees: 23,
            flipHorizontal: true,
            flipVertical: true,
            visible: false,
            enabled: false,
            fitMode: CameraElementFitMode.Contain);
        var template = new ViewTemplateDefinition("custom", "Custom", [slot]);

        var view = _factory.Instantiate(
            template,
            "Instantiated",
            new Dictionary<string, string?> { [slot.Id] = "camera-1" });
        var element = Assert.IsType<CameraElementDefinition>(Assert.Single(view.SceneElements));

        Assert.Equal((slot.X, slot.Y, slot.Width, slot.Height, slot.ZOrder),
            (element.X, element.Y, element.Width, element.Height, element.ZOrder));
        Assert.Equal((slot.CropLeft, slot.CropTop, slot.CropRight, slot.CropBottom),
            (element.CropLeft, element.CropTop, element.CropRight, element.CropBottom));
        Assert.Equal(slot.RotationDegrees, element.RotationDegrees);
        Assert.Equal(slot.FlipHorizontal, element.FlipHorizontal);
        Assert.Equal(slot.FlipVertical, element.FlipVertical);
        Assert.Equal(slot.Visible, element.Visible);
        Assert.Equal(slot.Enabled, element.Enabled);
        Assert.Equal(slot.FitMode, element.FitMode);
    }

    [Fact]
    public async Task SameCameraInMultipleTemplateSlotsKeepsSingleIngestOwnership()
    {
        var camera = Camera("camera-1", "Spot 1");
        var runtime = new FakeWorkspaceRuntimeService([camera]);
        runtime.CameraStates[camera.Id] = CameraRuntimeState.Receiving;
        await using var workspace = new WorkspaceViewModel(runtime);
        var template = Template("two-up-horizontal");
        var definition = _factory.Instantiate(
            template,
            "Repeated source",
            template.Slots.ToDictionary(slot => slot.Id, _ => (string?)camera.Id));

        Assert.True(await workspace.CreateViewAsync(definition));
        await workspace.RefreshNowAsync();

        Assert.Equal(2, workspace.SelectedView.Definition.SceneElements.Count);
        Assert.All(
            workspace.SelectedView.Definition.SceneElements.Cast<CameraElementDefinition>(),
            element => Assert.Equal(camera.Id, element.CameraId));
        Assert.Equal(1U, workspace.ActiveRtspSessionTotal);
        Assert.Equal(1U, workspace.ActiveDecoderTotal);
    }

    [Fact]
    public void DuplicateRegeneratesViewAndElementIdsWhilePreservingCompleteScene()
    {
        var sourceElement = new CameraElementDefinition(
            "source-element",
            "camera-1",
            -0.1,
            0.2,
            0.7,
            0.6,
            zOrder: 9,
            cropLeft: 0.1,
            cropTop: 0.2,
            cropRight: 0.15,
            cropBottom: 0.05,
            rotationDegrees: 37,
            flipHorizontal: true,
            flipVertical: true,
            visible: false,
            enabled: false,
            fitMode: CameraElementFitMode.Cover);
        var source = new ViewDefinition("source-view", "Source", [sourceElement]);

        var duplicate = _factory.Duplicate(source, "Copy");
        var copiedElement = Assert.IsType<CameraElementDefinition>(Assert.Single(duplicate.SceneElements));

        Assert.NotEqual(source.Id, duplicate.Id);
        Assert.NotEqual(sourceElement.Id, copiedElement.Id);
        Assert.Equal("camera-1", copiedElement.CameraId);
        Assert.Equal(
            (sourceElement.X, sourceElement.Y, sourceElement.Width, sourceElement.Height, sourceElement.ZOrder),
            (copiedElement.X, copiedElement.Y, copiedElement.Width, copiedElement.Height, copiedElement.ZOrder));
        Assert.Equal(
            (sourceElement.CropLeft, sourceElement.CropTop, sourceElement.CropRight, sourceElement.CropBottom),
            (copiedElement.CropLeft, copiedElement.CropTop, copiedElement.CropRight, copiedElement.CropBottom));
        Assert.Equal(sourceElement.RotationDegrees, copiedElement.RotationDegrees);
        Assert.Equal(sourceElement.FlipHorizontal, copiedElement.FlipHorizontal);
        Assert.Equal(sourceElement.FlipVertical, copiedElement.FlipVertical);
        Assert.Equal(sourceElement.Visible, copiedElement.Visible);
        Assert.Equal(sourceElement.Enabled, copiedElement.Enabled);
        Assert.Equal(sourceElement.FitMode, copiedElement.FitMode);
    }

    [Fact]
    public void DuplicatePreservesEveryVisualSubtypeAndReusesImageAssetIdentity()
    {
        var asset = new AssetDefinition("asset", "logo.png", AssetMediaType.Png, "/runtime/logo.png", 400, 200);
        var source = new ViewDefinition(
            "source",
            "Source",
            [
                new TextElementDefinition(
                    "text", "Title", 0, 0, 1, 0.1, 1,
                    verticalAlignment: TextElementVerticalAlignment.Bottom,
                    underline: true),
                new ImageElementDefinition("image", asset.Id, 0, 0.1, 0.2, 0.2, 2),
                new ShapeElementDefinition("shape", 0, 0.3, 1, 0.1, 3, 0x12345678),
                new FrameElementDefinition("frame", 0, 0, 1, 1, 4, 0xFFFFFFFF),
            ],
            [asset]);

        var duplicate = _factory.Duplicate(source, "Copy");

        Assert.Collection(
            duplicate.SceneElements,
            element =>
            {
                var text = Assert.IsType<TextElementDefinition>(element);
                Assert.Equal(TextElementVerticalAlignment.Bottom, text.VerticalAlignment);
                Assert.True(text.Underline);
            },
            element => Assert.Equal(asset.Id, Assert.IsType<ImageElementDefinition>(element).AssetId),
            element => Assert.IsType<ShapeElementDefinition>(element),
            element => Assert.IsType<FrameElementDefinition>(element));
        Assert.Equal(source.Assets, duplicate.Assets);
        Assert.Empty(source.SceneElements.Select(element => element.Id).Intersect(duplicate.SceneElements.Select(element => element.Id)));
    }

    [Fact]
    public async Task DuplicateRemainsIndependentAndDoesNotRerouteExistingOutput()
    {
        var camera = Camera("camera-1", "Spot 1");
        var originalElement = new CameraElementDefinition("original-element", camera.Id, 0.1, 0.2, 0.4, 0.4);
        var original = new ViewDefinition("original-view", "Original", [originalElement]);
        var output = new OutputDefinition("output", "Output", "ROBOCAM - ORIGINAL", original.Id);
        var runtime = new FakeWorkspaceRuntimeService([camera], views: [original], outputs: [output]);
        runtime.CameraStates[camera.Id] = CameraRuntimeState.Receiving;
        await using var workspace = new WorkspaceViewModel(runtime);
        var duplicate = _factory.Duplicate(original, "Copy");

        Assert.True(await workspace.CreateViewAsync(duplicate));
        var copiedElement = Assert.IsType<CameraElementDefinition>(Assert.Single(workspace.SelectedView.Definition.SceneElements));
        Assert.True(workspace.SelectedView.Editor.SelectElement(copiedElement.Id));
        Assert.True(await workspace.SelectedView.Editor.NudgeSelectedAsync(0.1, 0));

        Assert.Equal(0.1, originalElement.X);
        Assert.Equal(0.2, originalElement.Y);
        Assert.Equal(0.2, Assert.IsType<CameraElementDefinition>(
            workspace.SelectedView.Definition.SceneElements.Single()).X, 8);
        Assert.Equal(original.Id, workspace.Outputs.Single().Definition.ViewId);
        Assert.Equal(original.Id, runtime.OutputDefinitions.Single().ViewId);
        await workspace.RefreshNowAsync();
        Assert.Equal(1U, workspace.ActiveRtspSessionTotal);
        Assert.Equal(1U, workspace.ActiveDecoderTotal);
    }

    [Fact]
    public async Task PendingTemplateCameraSelectionsSurviveStatusPolling()
    {
        var cameras = new[] { Camera("camera-1", "Spot 1"), Camera("camera-2", "Spot 2") };
        var runtime = new FakeWorkspaceRuntimeService(cameras);
        await using var workspace = new WorkspaceViewModel(runtime);
        var draft = ViewCreationViewModel.Create(workspace.Cameras);
        draft.SelectedTemplateChoice = draft.TemplateChoices.Single(
            choice => choice.Template?.Id == "four-up");
        draft.SlotAssignments[0].SelectedCamera = workspace.Cameras[1];

        await workspace.RefreshNowAsync();

        Assert.Same(workspace.Cameras[1], draft.SlotAssignments[0].SelectedCamera);
        Assert.Null(draft.SlotAssignments[1].SelectedCamera);
    }

    [Fact]
    public async Task CreatedTemplateViewIsSelectedPreviewedAndFreelyEditable()
    {
        var camera = Camera("camera-1", "Spot 1");
        var runtime = new FakeWorkspaceRuntimeService([camera]);
        await using var workspace = new WorkspaceViewModel(runtime);
        var draft = ViewCreationViewModel.Create(workspace.Cameras);
        draft.ViewName = "Template View";
        draft.SelectedTemplateChoice = draft.TemplateChoices.Single(choice => choice.Template?.Id == "one-up");
        draft.SlotAssignments[0].SelectedCamera = workspace.Cameras[0];
        Assert.True(draft.TryBuildDefinition(_factory, out var definition));

        Assert.True(await workspace.CreateViewAsync(definition!));
        var editor = workspace.SelectedView.Editor;
        var element = Assert.Single(editor.Elements);
        Assert.True(editor.SelectElement(element.Id));
        Assert.True(await editor.NudgeSelectedAsync(0.05, 0.05));

        Assert.Same(workspace.Views[^1], workspace.SelectedView);
        Assert.Equal(workspace.SelectedView.Definition.Id, workspace.Preview.SelectedViewId);
        Assert.Equal(0.05, Assert.IsType<CameraElementDefinition>(
            workspace.SelectedView.Definition.SceneElements.Single()).X, 8);
        Assert.False(workspace.SelectedView.Definition.IsLegacyFourSlotLayout);
    }

    [Fact]
    public void CreationDraftAllowsCameraReplacementAndDuplicateCameraAssignments()
    {
        var cameras = new[]
        {
            new CameraItemViewModel(Camera("camera-1", "Spot 1"), new FakeWorkspaceRuntimeService(), new ImmediateUiDispatcher()),
            new CameraItemViewModel(Camera("camera-2", "Spot 2"), new FakeWorkspaceRuntimeService(), new ImmediateUiDispatcher()),
        };
        try
        {
            var draft = ViewCreationViewModel.Create(cameras);
            draft.SelectedTemplateChoice = draft.TemplateChoices.Single(
                choice => choice.Template?.Id == "two-up-horizontal");
            draft.SlotAssignments[0].SelectedCamera = cameras[0];
            draft.SlotAssignments[0].SelectedCamera = cameras[1];
            draft.SlotAssignments[1].SelectedCamera = cameras[1];

            draft.SlotAssignments[0].SelectedCamera = null;
            Assert.Null(draft.SlotAssignments[0].SelectedCamera);
            draft.SlotAssignments[0].SelectedCamera = cameras[1];

            Assert.True(draft.TryBuildDefinition(_factory, out var definition));
            Assert.All(
                definition!.SceneElements.Cast<CameraElementDefinition>(),
                element => Assert.Equal("camera-2", element.CameraId));
        }
        finally
        {
            foreach (var camera in cameras)
            {
                camera.Dispose();
            }
        }
    }

    private void AssertGeometry(
        string templateId,
        IReadOnlyList<(double X, double Y, double Width, double Height)> expected)
    {
        var template = Template(templateId);
        Assert.Equal(expected.Count, template.Slots.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var slot = template.Slots[index];
            Assert.Equal(expected[index], (slot.X, slot.Y, slot.Width, slot.Height));
            Assert.Equal(index, slot.ZOrder);
        }
    }

    private static ViewTemplateDefinition Template(string id)
        => BuiltInViewTemplates.All.Single(template => string.Equals(template.Id, id, StringComparison.Ordinal));

    private static CameraDefinition Camera(string id, string name)
        => new(id, name, "rtsp://127.0.0.1:8554/profile2/media.smp");
}
