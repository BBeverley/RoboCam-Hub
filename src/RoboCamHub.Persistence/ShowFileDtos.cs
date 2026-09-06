namespace RoboCamHub.Persistence;

internal sealed class ShowManifestV1
{
    public int SchemaVersion { get; set; }
    public required ShowDtoV1 Show { get; set; }
}

internal sealed class ShowDtoV1
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public List<CameraDtoV1> Cameras { get; set; } = [];
    public List<ViewDtoV1> Views { get; set; } = [];
    public List<AssetDtoV1> Assets { get; set; } = [];
    public List<OutputDtoV1> Outputs { get; set; } = [];
}

internal sealed class CameraDtoV1
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string RtspUrl { get; set; }
    public bool Enabled { get; set; }
    public uint ConnectTimeoutMs { get; set; }
}

internal sealed class ViewDtoV1
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public bool LegacyFourSlotLayout { get; set; }
    public List<string?> CameraIdsBySlot { get; set; } = [];
    public List<SceneElementDtoV1> SceneElements { get; set; } = [];
}

internal sealed class AssetDtoV1
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required string MediaType { get; set; }
    public required string ArchivePath { get; set; }
    public required string Sha256 { get; set; }
    public long ByteLength { get; set; }
    public uint PixelWidth { get; set; }
    public uint PixelHeight { get; set; }
}

internal sealed class OutputDtoV1
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string NdiSourceName { get; set; }
    public required string ViewId { get; set; }
    public bool Enabled { get; set; }
}

internal sealed class SceneElementDtoV1
{
    public required string Kind { get; set; }
    public required string Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int ZOrder { get; set; }
    public double RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool Visible { get; set; }
    public bool Enabled { get; set; }
    public string? CameraId { get; set; }
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
    public string? FitMode { get; set; }
    public string? Text { get; set; }
    public string? FontFamily { get; set; }
    public double FontSize { get; set; }
    public string? Alignment { get; set; }
    public string? VerticalAlignment { get; set; }
    public string? Weight { get; set; }
    public string? Style { get; set; }
    public bool Underline { get; set; }
    public uint TextColorRgba { get; set; }
    public uint? BackgroundColorRgba { get; set; }
    public string? AssetId { get; set; }
    public double Opacity { get; set; }
    public uint FillColorRgba { get; set; }
    public uint? OutlineColorRgba { get; set; }
    public double OutlineWidth { get; set; }
    public uint ColorRgba { get; set; }
    public double Thickness { get; set; }
}
