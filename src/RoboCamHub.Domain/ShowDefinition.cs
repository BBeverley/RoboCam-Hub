using System.Collections.ObjectModel;

namespace RoboCamHub.Domain;

public sealed class ShowDefinition
{
    public const int MaximumCameraCount = 64;
    public const int MaximumViewCount = 64;
    public const int MaximumOutputCount = 64;

    private readonly ReadOnlyCollection<CameraDefinition> _cameras;
    private readonly ReadOnlyCollection<ViewDefinition> _views;
    private readonly ReadOnlyCollection<OutputDefinition> _outputs;

    public ShowDefinition(
        string id,
        string name,
        IEnumerable<CameraDefinition> cameras,
        IEnumerable<ViewDefinition> views,
        IEnumerable<OutputDefinition> outputs,
        string? selectedViewId = null)
    {
        Id = DefinitionValidation.StableId(id, nameof(id), "Show ID");
        Name = DefinitionValidation.Required(name, nameof(name), "Show name");
        _cameras = ValidateCollection(cameras, MaximumCameraCount, "camera", nameof(cameras));
        _views = ValidateCollection(views, MaximumViewCount, "View", nameof(views));
        _outputs = ValidateCollection(outputs, MaximumOutputCount, "Output", nameof(outputs));
        if (_views.Count == 0)
        {
            throw new ArgumentException("A Show requires at least one View.", nameof(views));
        }

        ValidateReferences();
        SelectedViewId = selectedViewId is null
            ? _views[0].Id
            : DefinitionValidation.StableId(selectedViewId, nameof(selectedViewId), "Selected View ID");
        if (!_views.Any(view => string.Equals(view.Id, SelectedViewId, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"Selected View '{SelectedViewId}' is not part of the Show.",
                nameof(selectedViewId));
        }
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<CameraDefinition> Cameras => _cameras;

    public IReadOnlyList<ViewDefinition> Views => _views;

    public IReadOnlyList<OutputDefinition> Outputs => _outputs;

    public string SelectedViewId { get; }

    private static ReadOnlyCollection<T> ValidateCollection<T>(
        IEnumerable<T> source,
        int maximumCount,
        string itemName,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var items = source.ToArray();
        if (items.Length > maximumCount)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"A Show supports at most {maximumCount} {itemName}s.");
        }
        if (items.Any(item => item is null))
        {
            throw new ArgumentException($"Show {itemName}s must not contain null values.", parameterName);
        }

        var duplicate = items
            .GroupBy(GetId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"{itemName} ID '{duplicate.Key}' is duplicated.", parameterName);
        }
        return Array.AsReadOnly(items);

        static string GetId(T value) => value switch
        {
            CameraDefinition camera => camera.Id,
            ViewDefinition view => view.Id,
            OutputDefinition output => output.Id,
            _ => throw new InvalidOperationException($"Unsupported Show collection type '{typeof(T).Name}'."),
        };
    }

    private void ValidateReferences()
    {
        var cameraIds = _cameras.Select(camera => camera.Id).ToHashSet(StringComparer.Ordinal);
        var viewIds = _views.Select(view => view.Id).ToHashSet(StringComparer.Ordinal);
        var elementIds = new HashSet<string>(StringComparer.Ordinal);
        var assets = new Dictionary<string, AssetDefinition>(StringComparer.Ordinal);

        foreach (var view in _views)
        {
            foreach (var cameraId in view.CameraIdsBySlot.Where(id => id is not null))
            {
                if (!cameraIds.Contains(cameraId!))
                {
                    throw new ArgumentException($"View '{view.Id}' references missing camera '{cameraId}'.", nameof(Views));
                }
            }
            foreach (var element in view.SceneElements)
            {
                if (!elementIds.Add(element.Id))
                {
                    throw new ArgumentException($"Scene element ID '{element.Id}' is duplicated across Views.", nameof(Views));
                }
                if (element is CameraElementDefinition cameraElement && !cameraIds.Contains(cameraElement.CameraId))
                {
                    throw new ArgumentException(
                        $"View '{view.Id}' element '{element.Id}' references missing camera '{cameraElement.CameraId}'.",
                        nameof(Views));
                }
            }
            foreach (var asset in view.Assets)
            {
                if (assets.TryGetValue(asset.Id, out var existing))
                {
                    if (existing.DisplayName != asset.DisplayName
                        || existing.MediaType != asset.MediaType
                        || existing.PixelWidth != asset.PixelWidth
                        || existing.PixelHeight != asset.PixelHeight
                        || existing.RuntimeSourceReference != asset.RuntimeSourceReference)
                    {
                        throw new ArgumentException(
                            $"Asset ID '{asset.Id}' has conflicting definitions across Views.",
                            nameof(Views));
                    }
                }
                else
                {
                    assets.Add(asset.Id, asset);
                }
            }
        }

        foreach (var output in _outputs)
        {
            if (!viewIds.Contains(output.ViewId))
            {
                throw new ArgumentException(
                    $"Output '{output.Id}' references missing View '{output.ViewId}'.",
                    nameof(Outputs));
            }
        }
    }
}
