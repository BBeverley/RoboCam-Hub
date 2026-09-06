using System.Collections.ObjectModel;

namespace RoboCamHub.Domain;

public sealed class ViewDefinition
{
    public const int SlotCount = 4;

    private readonly ReadOnlyCollection<string?> _cameraIdsBySlot;
    private readonly ReadOnlyCollection<ViewSceneElementDefinition> _sceneElements;
    private readonly ReadOnlyCollection<AssetDefinition> _assets;

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
        _sceneElements = Array.AsReadOnly(
            _cameraIdsBySlot
                .Select((cameraId, slotIndex) => cameraId is null ? null : CreateLegacyElement(slotIndex, cameraId))
                .Where(element => element is not null)
                .Cast<ViewSceneElementDefinition>()
                .ToArray());
        _assets = Array.AsReadOnly(Array.Empty<AssetDefinition>());
        IsLegacyFourSlotLayout = true;
    }

    public ViewDefinition(
        string id,
        string name,
        IEnumerable<ViewSceneElementDefinition> sceneElements,
        IEnumerable<AssetDefinition>? assets = null)
    {
        Id = DefinitionValidation.StableId(id, nameof(id), "View ID");
        Name = DefinitionValidation.Required(name, nameof(name), "View name");
        ArgumentNullException.ThrowIfNull(sceneElements);

        var elements = sceneElements.ToArray();
        if (elements.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneElements), "A View supports at most 256 scene elements.");
        }

        if (elements.Any(element => element is null))
        {
            throw new ArgumentException("Scene elements must not contain null values.", nameof(sceneElements));
        }

        var duplicate = elements
            .GroupBy(element => element.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Scene element ID '{duplicate.Key}' is duplicated.",
                nameof(sceneElements));
        }

        _sceneElements = Array.AsReadOnly(elements);
        var assetArray = assets?.ToArray() ?? [];
        if (assetArray.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(assets), "A View supports at most 256 imported assets.");
        }
        if (assetArray.Any(asset => asset is null))
        {
            throw new ArgumentException("Assets must not contain null values.", nameof(assets));
        }

        var duplicateAsset = assetArray
            .GroupBy(asset => asset.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAsset is not null)
        {
            throw new ArgumentException($"Asset ID '{duplicateAsset.Key}' is duplicated.", nameof(assets));
        }

        var assetIds = assetArray.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var missingAsset = elements
            .OfType<ImageElementDefinition>()
            .FirstOrDefault(element => !assetIds.Contains(element.AssetId));
        if (missingAsset is not null)
        {
            throw new ArgumentException(
                $"Image element '{missingAsset.Id}' references missing asset '{missingAsset.AssetId}'.",
                nameof(sceneElements));
        }

        _assets = Array.AsReadOnly(assetArray);
        _cameraIdsBySlot = Array.AsReadOnly(new string?[SlotCount]);
        IsLegacyFourSlotLayout = false;
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<string?> CameraIdsBySlot => _cameraIdsBySlot;

    public IReadOnlyList<ViewSceneElementDefinition> SceneElements => _sceneElements;

    public IReadOnlyList<AssetDefinition> Assets => _assets;

    public bool IsLegacyFourSlotLayout { get; }

    public string? GetCameraId(int slotIndex)
    {
        if (slotIndex is < 0 or >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        return _cameraIdsBySlot[slotIndex];
    }

    private static CameraElementDefinition CreateLegacyElement(int slotIndex, string cameraId)
        => new(
            $"legacy-slot-{slotIndex}",
            cameraId,
            x: slotIndex % 2 * 0.5,
            y: slotIndex / 2 * 0.5,
            width: 0.5,
            height: 0.5,
            zOrder: slotIndex,
            fitMode: CameraElementFitMode.Stretch);
}
