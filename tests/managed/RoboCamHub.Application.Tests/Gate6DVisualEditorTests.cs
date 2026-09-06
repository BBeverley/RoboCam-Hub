using RoboCamHub.Domain;

namespace RoboCamHub.Application.Tests;

public sealed class Gate6DVisualEditorTests
{
    [Fact]
    public void ImageImportReadsPngAndJpegHeadersWithoutManagedPixelDecode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"robocamhub-gate6d-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var png = Path.Combine(root, "logo.png");
            File.WriteAllBytes(png,
            [
                137, 80, 78, 71, 13, 10, 26, 10,
                0, 0, 0, 13, 73, 72, 68, 82,
                0, 0, 3, 32, 0, 0, 1, 144,
            ]);
            var jpeg = Path.Combine(root, "logo.jpg");
            File.WriteAllBytes(jpeg,
            [
                0xFF, 0xD8,
                0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
                0xFF, 0xC0, 0x00, 0x11, 0x08, 0x02, 0xD0, 0x05, 0x00,
            ]);

            Assert.Equal((800U, 400U), ImageAssetMetadata.ReadDimensions(png, AssetMediaType.Png));
            Assert.Equal((1280U, 720U), ImageAssetMetadata.ReadDimensions(jpeg, AssetMediaType.Jpeg));
            Assert.ThrowsAny<IOException>(() =>
                ImageAssetMetadata.ReadDimensions(jpeg, AssetMediaType.Png));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ImageContainGeometryUsesImportedMetadataWithoutManagedPixels()
    {
        var asset = new AssetDefinition("asset", "logo.png", AssetMediaType.Png, "/runtime/logo.png", 800, 400);
        var image = new ImageElementDefinition("image", asset.Id, 0.25, 0.25, 0.5, 0.5, 0);
        var geometry = ViewEditorGeometry.Calculate(image, asset.PixelWidth, asset.PixelHeight);

        Assert.Equal(0.5, geometry.VisibleBounds.Width, 8);
        Assert.Equal(0.44444444, geometry.VisibleBounds.Height, 8);
        Assert.Equal(0.27777778, geometry.VisibleBounds.Y, 8);
    }

    [Fact]
    public async Task MixedHitTestingUsesZOrderAndFrameBorderOnly()
    {
        var rectangle = new ShapeElementDefinition("rectangle", 0.2, 0.2, 0.6, 0.6, 0, 0xFF0000FF);
        var frame = new FrameElementDefinition("frame", 0.2, 0.2, 0.6, 0.6, 10, 0xFFFFFFFF, 16);
        var runtime = new FakeWorkspaceRuntimeService(views: [new ViewDefinition("view", "View", [rectangle, frame])]);
        await using var workspace = new WorkspaceViewModel(runtime);
        var editor = workspace.SelectedView.Editor;

        Assert.Equal("rectangle", editor.HitTest(new EditorPoint(0.5, 0.5))?.Id);
        Assert.Equal("frame", editor.HitTest(new EditorPoint(0.202, 0.5))?.Id);
    }

    [Fact]
    public async Task NonCameraPendingMoveCommitsThroughAtomicSceneApply()
    {
        var text = new TextElementDefinition("title", "Title", 0.1, 0.1, 0.4, 0.2, 0);
        var runtime = new FakeWorkspaceRuntimeService(views: [new ViewDefinition("view", "View", [text])]);
        await using var workspace = new WorkspaceViewModel(runtime);
        var editor = workspace.SelectedView.Editor;

        Assert.True(editor.BeginMove("title", new EditorPoint(0.1, 0.1)));
        editor.UpdateMove(new EditorPoint(0.3, 0.4), snap: false);
        Assert.Equal(0, runtime.ApplyViewSceneCallCount);
        Assert.True(await editor.CommitInteractionAsync());

        var applied = Assert.IsType<TextElementDefinition>(Assert.Single(runtime.LastAppliedScene!));
        Assert.Equal(0.3, applied.X, 8);
        Assert.Equal(0.4, applied.Y, 8);
    }

    [Fact]
    public async Task AddAndDuplicateVisualElementsPreserveTypeAndImageAssetIdentity()
    {
        var runtime = new FakeWorkspaceRuntimeService();
        await using var workspace = new WorkspaceViewModel(runtime);
        var editor = workspace.SelectedView.Editor;
        var asset = new AssetDefinition("asset", "logo.png", AssetMediaType.Png, "/runtime/logo.png", 2, 1);

        Assert.True(await editor.AddTextAsync());
        Assert.True(await editor.AddRectangleAsync());
        Assert.True(await editor.AddFrameAsync());
        Assert.True(await editor.AddImageAsync(asset));
        Assert.True(await editor.DuplicateSelectedAsync());

        Assert.Equal(5, editor.Elements.Count);
        var images = editor.Elements.Select(item => item.Definition).OfType<ImageElementDefinition>().ToArray();
        Assert.Equal(2, images.Length);
        Assert.All(images, image => Assert.Equal(asset.Id, image.AssetId));
        Assert.Single(workspace.SelectedView.Definition.Assets);
    }

    [Fact]
    public void VisualPropertyEditorValidatesRgbaAndPreservesSubtype()
    {
        var source = new ShapeElementDefinition("shape", 0, 0, 1, 1, 0, 0x11223344);
        var properties = new VisualElementPropertiesViewModel(source)
        {
            PrimaryColor = "#AABBCCDD",
            SecondaryColor = "#01020304",
            StrokeWidth = 7,
        };

        var updated = Assert.IsType<ShapeElementDefinition>(properties.ToDefinition());
        Assert.Equal(0xAABBCCDDU, updated.FillColorRgba);
        Assert.Equal(0x01020304U, updated.OutlineColorRgba);
        Assert.Equal(7, updated.OutlineWidth);
        properties.PrimaryColor = "red";
        Assert.Throws<FormatException>(() => properties.ToDefinition());
    }
}
