using System.Collections.ObjectModel;
using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public static class BuiltInViewTemplates
{
    private static readonly ReadOnlyCollection<ViewTemplateDefinition> Definitions = Array.AsReadOnly(
    [
        Grid("one-up", "1-Up", columns: 1, rows: 1, slotCount: 1),
        Grid("two-up-horizontal", "2-Up Horizontal", columns: 2, rows: 1, slotCount: 2),
        Grid("two-up-vertical", "2-Up Vertical", columns: 1, rows: 2, slotCount: 2),
        Grid("three-up", "3-Up", columns: 3, rows: 1, slotCount: 3),
        Grid("four-up", "4-Up / 2×2", columns: 2, rows: 2, slotCount: 4),
        Grid("eight-up", "4×2", columns: 4, rows: 2, slotCount: 8),
        new ViewTemplateDefinition(
            "picture-in-picture",
            "Picture-in-Picture",
            [
                Slot(0, 0, 0, 1, 1, zOrder: 0, displayLabel: "Main"),
                Slot(1, 0.67, 0.67, 0.3, 0.3, zOrder: 1, displayLabel: "Inset"),
            ]),
    ]);

    public static IReadOnlyList<ViewTemplateDefinition> All => Definitions;

    private static ViewTemplateDefinition Grid(
        string id,
        string name,
        int columns,
        int rows,
        int slotCount)
    {
        var slots = new List<ViewTemplateSlotDefinition>(slotCount);
        for (var index = 0; index < slotCount; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var left = column / (double)columns;
            var right = (column + 1) / (double)columns;
            var top = row / (double)rows;
            var bottom = (row + 1) / (double)rows;
            slots.Add(Slot(
                index,
                left,
                top,
                right - left,
                bottom - top,
                zOrder: index,
                displayLabel: $"Camera {index + 1}"));
        }
        return new ViewTemplateDefinition(id, name, slots);
    }

    private static ViewTemplateSlotDefinition Slot(
        int index,
        double x,
        double y,
        double width,
        double height,
        int zOrder,
        string displayLabel)
        => new(
            $"slot-{index + 1}",
            x,
            y,
            width,
            height,
            zOrder,
            displayLabel,
            fitMode: CameraElementFitMode.Stretch);
}

public sealed class ViewTemplateFactory
{
    public ViewDefinition CreateBlank(string name)
        => new(NewId("view"), name.Trim(), Array.Empty<ViewSceneElementDefinition>());

    public ViewDefinition Instantiate(
        ViewTemplateDefinition template,
        string name,
        IReadOnlyDictionary<string, string?> cameraIdsBySlot)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(cameraIdsBySlot);

        var slotIds = template.Slots.Select(slot => slot.Id).ToHashSet(StringComparer.Ordinal);
        var unknownSlot = cameraIdsBySlot.Keys.FirstOrDefault(slotId => !slotIds.Contains(slotId));
        if (unknownSlot is not null)
        {
            throw new ArgumentException(
                $"Template assignment references unknown slot '{unknownSlot}'.",
                nameof(cameraIdsBySlot));
        }

        var elements = new List<ViewSceneElementDefinition>();
        foreach (var slot in template.Slots)
        {
            if (!cameraIdsBySlot.TryGetValue(slot.Id, out var cameraId)
                || string.IsNullOrWhiteSpace(cameraId))
            {
                continue;
            }

            elements.Add(CreateElement(slot, cameraId));
        }
        return new ViewDefinition(NewId("view"), name.Trim(), elements);
    }

    public ViewDefinition Duplicate(ViewDefinition source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        var elements = source.SceneElements.Select(DuplicateElement).ToArray();
        return new ViewDefinition(NewId("view"), name.Trim(), elements, source.Assets);
    }

    private static CameraElementDefinition CreateElement(
        ViewTemplateSlotDefinition slot,
        string cameraId)
        => new(
            NewId("camera-element"),
            cameraId,
            slot.X,
            slot.Y,
            slot.Width,
            slot.Height,
            slot.ZOrder,
            slot.CropLeft,
            slot.CropTop,
            slot.CropRight,
            slot.CropBottom,
            slot.RotationDegrees,
            slot.FlipHorizontal,
            slot.FlipVertical,
            slot.Visible,
            slot.Enabled,
            slot.FitMode);

    private static ViewSceneElementDefinition DuplicateElement(ViewSceneElementDefinition source)
        => source switch
        {
            CameraElementDefinition camera => new CameraElementDefinition(
                NewId("camera-element"),
                camera.CameraId,
                camera.X,
                camera.Y,
                camera.Width,
                camera.Height,
                camera.ZOrder,
                camera.CropLeft,
                camera.CropTop,
                camera.CropRight,
                camera.CropBottom,
                camera.RotationDegrees,
                camera.FlipHorizontal,
                camera.FlipVertical,
                camera.Visible,
                camera.Enabled,
                camera.FitMode),
            TextElementDefinition text => new TextElementDefinition(
                NewId("text-element"), text.Text, text.X, text.Y, text.Width, text.Height,
                text.ZOrder, text.FontFamily, text.FontSize, text.Alignment, text.Weight,
                text.Style, text.TextColorRgba, text.BackgroundColorRgba, text.RotationDegrees,
                text.FlipHorizontal, text.FlipVertical, text.Visible, text.Enabled,
                text.VerticalAlignment, text.Underline),
            ImageElementDefinition image => new ImageElementDefinition(
                NewId("image-element"), image.AssetId, image.X, image.Y, image.Width,
                image.Height, image.ZOrder, image.FitMode, image.Opacity, image.RotationDegrees,
                image.FlipHorizontal, image.FlipVertical, image.Visible, image.Enabled),
            ShapeElementDefinition rectangle => new ShapeElementDefinition(
                NewId("rectangle-element"), rectangle.X, rectangle.Y, rectangle.Width,
                rectangle.Height, rectangle.ZOrder, rectangle.FillColorRgba,
                rectangle.OutlineColorRgba, rectangle.OutlineWidth, rectangle.Opacity,
                rectangle.RotationDegrees, rectangle.Visible, rectangle.Enabled),
            FrameElementDefinition frame => new FrameElementDefinition(
                NewId("frame-element"), frame.X, frame.Y, frame.Width, frame.Height,
                frame.ZOrder, frame.ColorRgba, frame.Thickness, frame.Opacity,
                frame.RotationDegrees, frame.Visible, frame.Enabled),
            _ => throw new NotSupportedException(
                $"Scene element type '{source.GetType().Name}' cannot be duplicated by Gate 6C."),
        };

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
