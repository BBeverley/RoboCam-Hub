using RoboCamHub.Domain;

namespace RoboCamHub.Domain.Tests;

public sealed class ViewTemplateDefinitionTests
{
    [Fact]
    public void TemplateSlotsArePortableValidatedPlaceholdersWithoutCameraIdentity()
    {
        var slot = new ViewTemplateSlotDefinition(
            "slot-main",
            0.1,
            0.2,
            0.7,
            0.6,
            zOrder: 4,
            displayLabel: "Main camera",
            cropLeft: 0.1,
            cropTop: 0.05,
            cropRight: 0.2,
            cropBottom: 0.15,
            rotationDegrees: 12,
            flipHorizontal: true,
            flipVertical: true,
            visible: false,
            fitMode: CameraElementFitMode.Cover);
        var template = new ViewTemplateDefinition("template-main", "Main", [slot]);

        Assert.Equal("slot-main", template.Slots.Single().Id);
        Assert.Equal("Main camera", slot.DisplayLabel);
        Assert.Equal((0.1, 0.2, 0.7, 0.6), (slot.X, slot.Y, slot.Width, slot.Height));
        Assert.Equal(4, slot.ZOrder);
        Assert.Equal(0.1, slot.CropLeft);
        Assert.Equal(12, slot.RotationDegrees);
        Assert.True(slot.FlipHorizontal);
        Assert.True(slot.FlipVertical);
        Assert.False(slot.Visible);
        Assert.Equal(CameraElementFitMode.Cover, slot.FitMode);
        Assert.DoesNotContain(
            typeof(ViewTemplateSlotDefinition).GetProperties(),
            property => string.Equals(property.Name, "CameraId", StringComparison.Ordinal));
        Assert.False(template.Slots is ViewTemplateSlotDefinition[]);
    }

    [Fact]
    public void TemplateValidationRejectsDuplicateSlotsAndInvalidSceneGeometry()
    {
        var slot = new ViewTemplateSlotDefinition("slot", 0, 0, 1, 1);

        Assert.Throws<ArgumentException>(() => new ViewTemplateDefinition("template", "Template", [slot, slot]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewTemplateSlotDefinition(
            "invalid-width", 0, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewTemplateSlotDefinition(
            "invalid-position", double.NaN, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => new ViewTemplateSlotDefinition(
            "invalid-crop", 0, 0, 1, 1, cropLeft: 0.5, cropRight: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewTemplateSlotDefinition(
            "invalid-rotation", 0, 0, 1, 1, rotationDegrees: 361));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewTemplateSlotDefinition(
            "invalid-fit", 0, 0, 1, 1, fitMode: (CameraElementFitMode)99));
    }
}
