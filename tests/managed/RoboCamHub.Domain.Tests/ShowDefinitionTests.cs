using RoboCamHub.Domain;

namespace RoboCamHub.Domain.Tests;

public sealed class ShowDefinitionTests
{
    [Fact]
    public void CompleteShowPreservesStableIdsAndValidatesReferences()
    {
        var camera = new CameraDefinition("camera", "Camera", "rtsp://10.0.0.1/stream");
        var element = new CameraElementDefinition("element", camera.Id, 0, 0, 1, 1);
        var view = new ViewDefinition("view", "View", [element]);
        var output = new OutputDefinition("output", "Output", "ROBOCAM - TEST", view.Id);

        var show = new ShowDefinition("show", "Show", [camera], [view], [output], view.Id);

        Assert.Equal(("show", "camera", "view", "element", "output"),
            (show.Id, show.Cameras[0].Id, show.Views[0].Id, show.Views[0].SceneElements[0].Id, show.Outputs[0].Id));
        Assert.Equal("view", show.SelectedViewId);
    }

    [Fact]
    public void DuplicateAndMissingStableReferencesAreRejected()
    {
        var camera = new CameraDefinition("camera", "Camera", "rtsp://10.0.0.1/stream");
        var view = new ViewDefinition("view", "View");
        Assert.Throws<ArgumentException>(() => new ShowDefinition("show", "Show", [camera, camera], [view], []));
        Assert.Throws<ArgumentException>(() => new ShowDefinition(
            "show", "Show", [camera], [new ViewDefinition("view", "View", "missing")], []));
        Assert.Throws<ArgumentException>(() => new ShowDefinition(
            "show", "Show", [camera], [view], [new OutputDefinition("output", "Output", "NDI", "missing")]));
        Assert.Throws<ArgumentException>(() => new ShowDefinition("show", "Show", [camera], [view], [], "missing"));
    }

    [Fact]
    public void ElementIdsAreUniqueAcrossTheWholeShow()
    {
        var first = new ViewDefinition("a", "A", [new TextElementDefinition("same", "A", 0, 0, 1, 1, 0)]);
        var second = new ViewDefinition("b", "B", [new TextElementDefinition("same", "B", 0, 0, 1, 1, 0)]);

        var error = Assert.Throws<ArgumentException>(() => new ShowDefinition("show", "Show", [], [first, second], []));

        Assert.Contains("duplicated across Views", error.Message, StringComparison.Ordinal);
    }
}
