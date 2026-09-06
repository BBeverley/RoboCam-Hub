namespace RoboCamHub.Domain;

public enum AssetMediaType
{
    Png = 0,
    Jpeg = 1,
}

public sealed class AssetDefinition
{
    public AssetDefinition(
        string id,
        string displayName,
        AssetMediaType mediaType,
        string runtimeSourceReference,
        uint pixelWidth = 0,
        uint pixelHeight = 0)
    {
        Id = DefinitionValidation.StableId(id, nameof(id), "Asset ID");
        DisplayName = DefinitionValidation.Required(displayName, nameof(displayName), "Asset display name");
        if (!Enum.IsDefined(mediaType))
        {
            throw new ArgumentOutOfRangeException(nameof(mediaType));
        }
        MediaType = mediaType;
        RuntimeSourceReference = DefinitionValidation.Required(
            runtimeSourceReference,
            nameof(runtimeSourceReference),
            "Asset runtime source reference");
        if ((pixelWidth == 0) != (pixelHeight == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Asset dimensions must both be zero or both be positive.");
        }
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public AssetMediaType MediaType { get; }

    // This is transient import/runtime metadata. Scene elements persist only AssetId.
    public string RuntimeSourceReference { get; }

    public uint PixelWidth { get; }

    public uint PixelHeight { get; }
}
