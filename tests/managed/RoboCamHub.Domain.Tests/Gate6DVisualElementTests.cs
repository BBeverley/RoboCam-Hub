using RoboCamHub.Domain;

namespace RoboCamHub.Domain.Tests;

public sealed class Gate6DVisualElementTests
{
    [Fact]
    public void MixedSceneTypesRetainStableIdsAndAssetReferences()
    {
        var asset = new AssetDefinition("asset-logo", "logo.png", AssetMediaType.Png, "/runtime/logo.png", 800, 400);
        ViewSceneElementDefinition[] elements =
        [
            new CameraElementDefinition("camera", "camera-1", 0, 0, 1, 1),
            new TextElementDefinition(
                "text", "RoboCam ✓", 0.1, 0.1, 0.8, 0.2, 3,
                verticalAlignment: TextElementVerticalAlignment.Bottom,
                underline: true),
            new ImageElementDefinition("image", asset.Id, 0.7, 0.7, 0.2, 0.2, 4),
            new ShapeElementDefinition("rectangle", 0, 0, 1, 0.1, -1, 0x102030FF),
            new FrameElementDefinition("frame", 0, 0, 1, 1, 5, 0xFFFFFFFF),
        ];

        var view = new ViewDefinition("view", "Mixed", elements, [asset]);

        Assert.Equal(new[] { "camera", "text", "image", "rectangle", "frame" }, view.SceneElements.Select(item => item.Id));
        Assert.Equal("asset-logo", Assert.IsType<ImageElementDefinition>(view.SceneElements[2]).AssetId);
        var text = Assert.IsType<TextElementDefinition>(view.SceneElements[1]);
        Assert.Equal(TextElementVerticalAlignment.Bottom, text.VerticalAlignment);
        Assert.True(text.Underline);
        Assert.DoesNotContain("/runtime/logo.png", view.SceneElements.SelectMany(item => item.GetType().GetProperties().Select(property => property.GetValue(item)?.ToString())));
    }

    [Fact]
    public void MixedDuplicateElementIdsAreRejected()
    {
        var camera = new CameraElementDefinition("duplicate", "camera-1", 0, 0, 1, 1);
        var text = new TextElementDefinition("duplicate", "Title", 0, 0, 1, 1, 1);

        Assert.Throws<ArgumentException>(() => new ViewDefinition("view", "View", [camera, text]));
    }

    [Fact]
    public void MissingAssetAndInvalidVisualValuesAreRejected()
    {
        var image = new ImageElementDefinition("image", "missing", 0, 0, 1, 1, 0);
        Assert.Throws<ArgumentException>(() => new ViewDefinition("view", "View", [image]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameElementDefinition("frame", 0, 0, 1, 1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShapeElementDefinition("shape", 0, 0, 1, 1, 0, 0, opacity: 1.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextElementDefinition("text", "Title", 0, 0, 1, 1, 0, fontSize: 0));
    }
}
