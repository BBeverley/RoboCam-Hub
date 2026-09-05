using System.Collections.ObjectModel;

namespace RoboCamHub.Domain;

public sealed class ViewDefinition
{
    public const int SlotCount = 4;

    private readonly ReadOnlyCollection<string?> _cameraIdsBySlot;

    public ViewDefinition(
        string id,
        string name,
        string? slot0CameraId = null,
        string? slot1CameraId = null,
        string? slot2CameraId = null,
        string? slot3CameraId = null)
    {
        Id = DefinitionValidation.StableId(id, nameof(id), "View ID");
        Name = DefinitionValidation.Required(name, nameof(name), "View name");
        _cameraIdsBySlot = Array.AsReadOnly(
        [
            DefinitionValidation.OptionalStableId(slot0CameraId, nameof(slot0CameraId), "Slot 0 camera ID"),
            DefinitionValidation.OptionalStableId(slot1CameraId, nameof(slot1CameraId), "Slot 1 camera ID"),
            DefinitionValidation.OptionalStableId(slot2CameraId, nameof(slot2CameraId), "Slot 2 camera ID"),
            DefinitionValidation.OptionalStableId(slot3CameraId, nameof(slot3CameraId), "Slot 3 camera ID"),
        ]);
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<string?> CameraIdsBySlot => _cameraIdsBySlot;

    public string? GetCameraId(int slotIndex)
    {
        if (slotIndex is < 0 or >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        return _cameraIdsBySlot[slotIndex];
    }
}
