using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoboCamHub.Domain;

namespace RoboCamHub.Persistence;

public sealed class ShowFileService
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultExtension = ".rchshow";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };

    private readonly string _assetCacheRoot;
    private readonly IAtomicFileCommitter _committer;

    public ShowFileService(string? assetCacheRoot = null, IAtomicFileCommitter? committer = null)
    {
        _assetCacheRoot = assetCacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoboCam-Hub",
            "AssetCache");
        _committer = committer ?? new AtomicFileCommitter();
    }

    public Task SaveAsync(ShowDefinition show, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(EnsureExtension(path));
        return Task.Run(() => SaveCore(show, fullPath, cancellationToken), cancellationToken);
    }

    public Task<ShowLoadResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return Task.Run(() => LoadCore(fullPath, cancellationToken), cancellationToken);
    }

    public static string EnsureExtension(string path)
        => string.Equals(Path.GetExtension(path), DefaultExtension, StringComparison.OrdinalIgnoreCase)
            ? path
            : path + DefaultExtension;

    private void SaveCore(ShowDefinition show, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCredentialSafety(show);
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new ShowFileException("The show file destination has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        var backupPath = destinationPath + ".bak";
        try
        {
            var manifest = ToManifest(show, cancellationToken);
            using (var file = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.SequentialScan))
            {
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteManifest(archive, manifest);
                    WriteAssets(archive, manifest.Show.Assets, show, cancellationToken);
                }
                file.Flush(flushToDisk: true);
            }

            ValidateArchive(temporaryPath, materializeDirectory: null, cancellationToken);
            _committer.Commit(temporaryPath, destinationPath, backupPath);
        }
        catch (ShowFileException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ShowFileException($"Saving show '{destinationPath}' failed: {exception.Message}", exception);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private ShowLoadResult LoadCore(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new ShowFileException($"Show file '{path}' does not exist.");
        }

        Directory.CreateDirectory(_assetCacheRoot);
        var materializeDirectory = Path.Combine(_assetCacheRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(materializeDirectory);
        try
        {
            return ValidateArchive(path, materializeDirectory, cancellationToken);
        }
        catch
        {
            TryDeleteDirectory(materializeDirectory);
            throw;
        }
    }

    private static ShowLoadResult ValidateArchive(
        string path,
        string? materializeDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
            ValidateEntries(archive);
            var manifestEntry = archive.GetEntry("manifest.json")
                ?? throw new ShowFileException("The .rchshow archive does not contain manifest.json.");
            ShowManifestV1 manifest;
            using (var stream = manifestEntry.Open())
            {
                manifest = JsonSerializer.Deserialize<ShowManifestV1>(stream, JsonOptions)
                    ?? throw new ShowFileException("manifest.json is empty.");
            }
            if (manifest.SchemaVersion > CurrentSchemaVersion)
            {
                throw new ShowFileException(
                    $"This show uses unsupported schema version {manifest.SchemaVersion}; this build supports up to version {CurrentSchemaVersion}.");
            }
            if (manifest.SchemaVersion < 1)
            {
                throw new ShowFileException($"Schema version {manifest.SchemaVersion} is invalid or unsupported.");
            }

            // Version dispatch is deliberately explicit so later readers/migrations can be
            // registered here without changing archive validation or the public loader.
            return manifest.SchemaVersion switch
            {
                1 => ReadV1(archive, manifest, materializeDirectory, cancellationToken),
                _ => throw new ShowFileException($"No reader is registered for schema version {manifest.SchemaVersion}."),
            };
        }
        catch (ShowFileException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new ShowFileException("The .rchshow archive is truncated or malformed.", exception);
        }
        catch (JsonException exception)
        {
            throw new ShowFileException($"manifest.json is malformed: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ShowFileException($"Opening show '{path}' failed: {exception.Message}", exception);
        }
    }

    private static ShowLoadResult ReadV1(
        ZipArchive archive,
        ShowManifestV1 manifest,
        string? materializeDirectory,
        CancellationToken cancellationToken)
    {
        var showDto = manifest.Show ?? throw new ShowFileException("manifest.json has no Show object.");
        RejectDuplicates(showDto.Cameras, camera => camera.Id, "camera");
        RejectDuplicates(showDto.Views, view => view.Id, "View");
        RejectDuplicates(showDto.Outputs, output => output.Id, "Output");
        RejectDuplicates(showDto.Assets, asset => asset.Id, "asset");

        var warnings = new List<ShowLoadWarning>();
        var declaredAssetIds = showDto.Assets.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var assets = new Dictionary<string, AssetDefinition>(StringComparer.Ordinal);
        foreach (var assetDto in showDto.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mediaType = ParseMediaType(assetDto.MediaType);
            var expectedPath = GetArchiveAssetPath(assetDto.Id, mediaType);
            if (!string.Equals(assetDto.ArchivePath, expectedPath, StringComparison.Ordinal))
            {
                throw new ShowFileException(
                    $"Asset '{assetDto.Id}' uses illegal archive path '{assetDto.ArchivePath}'.");
            }
            if (assetDto.ByteLength is < 1 or > ShowFileLimits.MaximumAssetBytes)
            {
                throw new ShowFileException($"Asset '{assetDto.Id}' has an invalid declared size.");
            }
            if (!IsLowerHexSha256(assetDto.Sha256))
            {
                throw new ShowFileException($"Asset '{assetDto.Id}' has an invalid SHA-256 value.");
            }

            var entry = archive.GetEntry(expectedPath);
            if (entry is null)
            {
                warnings.Add(new ShowLoadWarning(
                    "missing-asset",
                    $"Asset '{assetDto.DisplayName}' is missing; affected image elements were omitted."));
                continue;
            }
            if (entry.Length != assetDto.ByteLength)
            {
                warnings.Add(new ShowLoadWarning(
                    "corrupt-asset",
                    $"Asset '{assetDto.DisplayName}' has an unexpected size; affected image elements were omitted."));
                continue;
            }

            if (materializeDirectory is null)
            {
                if (!ValidateAssetStream(entry, assetDto, mediaType, outputPath: null, cancellationToken))
                {
                    throw new ShowFileException($"Embedded asset '{assetDto.Id}' failed integrity validation.");
                }

                continue;
            }

            var outputPath = Path.Combine(materializeDirectory, Path.GetFileName(expectedPath));
            if (!ValidateAssetStream(entry, assetDto, mediaType, outputPath, cancellationToken))
            {
                warnings.Add(new ShowLoadWarning(
                    "corrupt-asset",
                    $"Asset '{assetDto.DisplayName}' failed integrity or media validation; affected image elements were omitted."));
                continue;
            }
            assets.Add(assetDto.Id, new AssetDefinition(
                assetDto.Id,
                assetDto.DisplayName,
                mediaType,
                outputPath,
                assetDto.PixelWidth,
                assetDto.PixelHeight));
        }

        var declaredArchivePaths = showDto.Assets
            .Select(asset => asset.ArchivePath)
            .Append("manifest.json")
            .ToHashSet(StringComparer.Ordinal);
        var unexpectedEntry = archive.Entries.FirstOrDefault(entry => !declaredArchivePaths.Contains(entry.FullName));
        if (unexpectedEntry is not null)
        {
            throw new ShowFileException($"Archive entry '{unexpectedEntry.FullName}' is not declared by manifest.json.");
        }

        if (materializeDirectory is null)
        {
            return new ShowLoadResult(
                new ShowDefinition("validation", "Validation", [], [new ViewDefinition("validation", "Validation")], []),
                [],
                string.Empty);
        }

        var cameras = showDto.Cameras.Select(ToDomain).ToArray();
        ValidateCredentialSafety(cameras);
        var views = showDto.Views.Select(view => ToDomain(view, assets, declaredAssetIds, warnings)).ToArray();
        var outputs = showDto.Outputs.Select(ToDomain).ToArray();
        ShowDefinition show;
        try
        {
            show = new ShowDefinition(
                showDto.Id,
                showDto.Name,
                cameras,
                views,
                outputs,
                views.FirstOrDefault()?.Id);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ShowFileException($"The show manifest failed validation: {exception.Message}", exception);
        }
        return new ShowLoadResult(show, warnings.AsReadOnly(), materializeDirectory);
    }

    private static void ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > ShowFileLimits.MaximumArchiveEntries)
        {
            throw new ShowFileException("The .rchshow archive has an invalid number of entries.");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        long expandedTotal = 0;
        foreach (var entry in archive.Entries)
        {
            if (!names.Add(entry.FullName))
            {
                throw new ShowFileException($"Archive entry '{entry.FullName}' is duplicated.");
            }
            if (!IsSafeArchivePath(entry.FullName))
            {
                throw new ShowFileException($"Archive entry '{entry.FullName}' uses an illegal path.");
            }
            if (IsSymbolicLink(entry))
            {
                throw new ShowFileException($"Archive entry '{entry.FullName}' is a symbolic link.");
            }
            if (entry.FullName == "manifest.json" && entry.Length > ShowFileLimits.MaximumManifestBytes)
            {
                throw new ShowFileException("manifest.json exceeds the allowed size.");
            }
            if (entry.FullName.StartsWith("assets/", StringComparison.Ordinal)
                && entry.Length > ShowFileLimits.MaximumAssetBytes)
            {
                throw new ShowFileException($"Asset entry '{entry.FullName}' exceeds the allowed size.");
            }
            expandedTotal = checked(expandedTotal + entry.Length);
            if (expandedTotal > ShowFileLimits.MaximumExpandedArchiveBytes)
            {
                throw new ShowFileException("The expanded .rchshow archive exceeds the allowed size.");
            }
            if (entry.Length > 0
                && (entry.CompressedLength == 0
                    || entry.Length / (double)entry.CompressedLength > ShowFileLimits.MaximumCompressionRatio))
            {
                throw new ShowFileException($"Archive entry '{entry.FullName}' has an unsafe compression ratio.");
            }
        }
    }

    private static ShowManifestV1 ToManifest(ShowDefinition show, CancellationToken cancellationToken)
    {
        var assets = new Dictionary<string, AssetDefinition>(StringComparer.Ordinal);
        foreach (var asset in show.Views.SelectMany(view => view.Assets))
        {
            if (assets.TryGetValue(asset.Id, out var existing)
                && existing.RuntimeSourceReference != asset.RuntimeSourceReference)
            {
                throw new ShowFileException($"Asset '{asset.Id}' resolves to conflicting runtime sources.");
            }
            assets[asset.Id] = asset;
        }

        return new ShowManifestV1
        {
            SchemaVersion = CurrentSchemaVersion,
            Show = new ShowDtoV1
            {
                Id = show.Id,
                Name = show.Name,
                Cameras = show.Cameras.Select(camera => new CameraDtoV1
                {
                    Id = camera.Id,
                    Name = camera.Name,
                    RtspUrl = camera.RtspUrl,
                    Enabled = camera.Enabled,
                    ConnectTimeoutMs = camera.ConnectTimeoutMs,
                }).ToList(),
                Views = show.Views.Select(ToDto).ToList(),
                Assets = assets.Values.OrderBy(asset => asset.Id, StringComparer.Ordinal).Select(asset =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = new FileInfo(asset.RuntimeSourceReference);
                    if (!info.Exists)
                    {
                        throw new ShowFileException(
                            $"Asset '{asset.DisplayName}' cannot be saved because its runtime source is missing.");
                    }
                    if (info.Length is < 1 or > ShowFileLimits.MaximumAssetBytes)
                    {
                        throw new ShowFileException($"Asset '{asset.DisplayName}' exceeds the allowed size.");
                    }
                    ValidateMediaFile(info.FullName, asset.MediaType);
                    return new AssetDtoV1
                    {
                        Id = asset.Id,
                        DisplayName = asset.DisplayName,
                        MediaType = FormatMediaType(asset.MediaType),
                        ArchivePath = GetArchiveAssetPath(asset.Id, asset.MediaType),
                        Sha256 = ComputeSha256(info.FullName),
                        ByteLength = info.Length,
                        PixelWidth = asset.PixelWidth,
                        PixelHeight = asset.PixelHeight,
                    };
                }).ToList(),
                Outputs = show.Outputs.Select(output => new OutputDtoV1
                {
                    Id = output.Id,
                    Name = output.Name,
                    NdiSourceName = output.NdiSourceName,
                    ViewId = output.ViewId,
                    Enabled = output.Enabled,
                }).ToList(),
            },
        };
    }

    private static ViewDtoV1 ToDto(ViewDefinition view)
        => new()
        {
            Id = view.Id,
            Name = view.Name,
            LegacyFourSlotLayout = view.IsLegacyFourSlotLayout,
            CameraIdsBySlot = view.CameraIdsBySlot.ToList(),
            SceneElements = view.IsLegacyFourSlotLayout ? [] : view.SceneElements.Select(ToDto).ToList(),
        };

    private static SceneElementDtoV1 ToDto(ViewSceneElementDefinition element)
    {
        var dto = new SceneElementDtoV1
        {
            Kind = element switch
            {
                CameraElementDefinition => "camera",
                TextElementDefinition => "text",
                ImageElementDefinition => "image",
                ShapeElementDefinition => "rectangle",
                FrameElementDefinition => "frame",
                _ => throw new ShowFileException($"Scene element type '{element.GetType().Name}' is unsupported."),
            },
            Id = element.Id,
            X = element.X,
            Y = element.Y,
            Width = element.Width,
            Height = element.Height,
            ZOrder = element.ZOrder,
            RotationDegrees = element.RotationDegrees,
            FlipHorizontal = element.FlipHorizontal,
            FlipVertical = element.FlipVertical,
            Visible = element.Visible,
            Enabled = element.Enabled,
        };
        switch (element)
        {
            case CameraElementDefinition camera:
                dto.CameraId = camera.CameraId;
                dto.CropLeft = camera.CropLeft;
                dto.CropTop = camera.CropTop;
                dto.CropRight = camera.CropRight;
                dto.CropBottom = camera.CropBottom;
                dto.FitMode = camera.FitMode.ToString();
                break;
            case TextElementDefinition text:
                dto.Text = text.Text;
                dto.FontFamily = text.FontFamily;
                dto.FontSize = text.FontSize;
                dto.Alignment = text.Alignment.ToString();
                dto.VerticalAlignment = text.VerticalAlignment.ToString();
                dto.Weight = text.Weight.ToString();
                dto.Style = text.Style.ToString();
                dto.Underline = text.Underline;
                dto.TextColorRgba = text.TextColorRgba;
                dto.BackgroundColorRgba = text.BackgroundColorRgba;
                break;
            case ImageElementDefinition image:
                dto.AssetId = image.AssetId;
                dto.FitMode = image.FitMode.ToString();
                dto.Opacity = image.Opacity;
                break;
            case ShapeElementDefinition rectangle:
                dto.FillColorRgba = rectangle.FillColorRgba;
                dto.OutlineColorRgba = rectangle.OutlineColorRgba;
                dto.OutlineWidth = rectangle.OutlineWidth;
                dto.Opacity = rectangle.Opacity;
                break;
            case FrameElementDefinition frame:
                dto.ColorRgba = frame.ColorRgba;
                dto.Thickness = frame.Thickness;
                dto.Opacity = frame.Opacity;
                break;
        }
        return dto;
    }

    private static ViewDefinition ToDomain(
        ViewDtoV1 dto,
        IReadOnlyDictionary<string, AssetDefinition> assets,
        IReadOnlySet<string> declaredAssetIds,
        ICollection<ShowLoadWarning> warnings)
    {
        if (dto.LegacyFourSlotLayout)
        {
            if (dto.CameraIdsBySlot.Count != ViewDefinition.SlotCount || dto.SceneElements.Count != 0)
            {
                throw new ShowFileException($"Legacy View '{dto.Id}' has an invalid slot/scene representation.");
            }
            return new ViewDefinition(
                dto.Id,
                dto.Name,
                dto.CameraIdsBySlot[0],
                dto.CameraIdsBySlot[1],
                dto.CameraIdsBySlot[2],
                dto.CameraIdsBySlot[3]);
        }
        if (dto.CameraIdsBySlot.Any(id => id is not null))
        {
            throw new ShowFileException($"Freeform View '{dto.Id}' contains legacy slot assignments.");
        }

        var elements = new List<ViewSceneElementDefinition>();
        foreach (var elementDto in dto.SceneElements)
        {
            if (elementDto.Kind == "image" && elementDto.AssetId is not null
                && !declaredAssetIds.Contains(elementDto.AssetId))
            {
                throw new ShowFileException(
                    $"Image element '{elementDto.Id}' references undeclared asset '{elementDto.AssetId}'.");
            }
            if (elementDto.Kind == "image"
                && (elementDto.AssetId is null || !assets.ContainsKey(elementDto.AssetId)))
            {
                warnings.Add(new ShowLoadWarning(
                    "degraded-image-element",
                    $"Image element '{elementDto.Id}' in View '{dto.Name}' was omitted because its asset is unavailable."));
                continue;
            }
            elements.Add(ToDomain(elementDto));
        }
        var referencedAssetIds = elements.OfType<ImageElementDefinition>()
            .Select(element => element.AssetId)
            .ToHashSet(StringComparer.Ordinal);
        return new ViewDefinition(
            dto.Id,
            dto.Name,
            elements,
            referencedAssetIds.Select(id => assets[id]));
    }

    private static ViewSceneElementDefinition ToDomain(SceneElementDtoV1 dto)
        => dto.Kind switch
        {
            "camera" => new CameraElementDefinition(
                dto.Id, Required(dto.CameraId, "cameraId", dto.Id), dto.X, dto.Y, dto.Width, dto.Height,
                dto.ZOrder, dto.CropLeft, dto.CropTop, dto.CropRight, dto.CropBottom,
                dto.RotationDegrees, dto.FlipHorizontal, dto.FlipVertical, dto.Visible, dto.Enabled,
                ParseEnum<CameraElementFitMode>(dto.FitMode, "fitMode", dto.Id)),
            "text" => new TextElementDefinition(
                dto.Id, Required(dto.Text, "text", dto.Id), dto.X, dto.Y, dto.Width, dto.Height, dto.ZOrder,
                Required(dto.FontFamily, "fontFamily", dto.Id), dto.FontSize,
                ParseEnum<TextElementAlignment>(dto.Alignment, "alignment", dto.Id),
                ParseEnum<TextElementWeight>(dto.Weight, "weight", dto.Id),
                ParseEnum<TextElementStyle>(dto.Style, "style", dto.Id),
                dto.TextColorRgba, dto.BackgroundColorRgba, dto.RotationDegrees,
                dto.FlipHorizontal, dto.FlipVertical, dto.Visible, dto.Enabled,
                ParseEnum<TextElementVerticalAlignment>(dto.VerticalAlignment, "verticalAlignment", dto.Id),
                dto.Underline),
            "image" => new ImageElementDefinition(
                dto.Id, Required(dto.AssetId, "assetId", dto.Id), dto.X, dto.Y, dto.Width, dto.Height,
                dto.ZOrder, ParseEnum<CameraElementFitMode>(dto.FitMode, "fitMode", dto.Id), dto.Opacity,
                dto.RotationDegrees, dto.FlipHorizontal, dto.FlipVertical, dto.Visible, dto.Enabled),
            "rectangle" => new ShapeElementDefinition(
                dto.Id, dto.X, dto.Y, dto.Width, dto.Height, dto.ZOrder, dto.FillColorRgba,
                dto.OutlineColorRgba, dto.OutlineWidth, dto.Opacity, dto.RotationDegrees, dto.Visible, dto.Enabled),
            "frame" => new FrameElementDefinition(
                dto.Id, dto.X, dto.Y, dto.Width, dto.Height, dto.ZOrder, dto.ColorRgba,
                dto.Thickness, dto.Opacity, dto.RotationDegrees, dto.Visible, dto.Enabled),
            _ => throw new ShowFileException($"Scene element '{dto.Id}' has unknown kind '{dto.Kind}'."),
        };

    private static CameraDefinition ToDomain(CameraDtoV1 dto)
        => new(dto.Id, dto.Name, dto.RtspUrl, dto.Enabled, dto.ConnectTimeoutMs);

    private static OutputDefinition ToDomain(OutputDtoV1 dto)
        => new(dto.Id, dto.Name, dto.NdiSourceName, dto.ViewId, dto.Enabled);

    private static void WriteManifest(ZipArchive archive, ShowManifestV1 manifest)
    {
        var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, manifest, JsonOptions);
    }

    private static void WriteAssets(
        ZipArchive archive,
        IReadOnlyList<AssetDtoV1> assetDtos,
        ShowDefinition show,
        CancellationToken cancellationToken)
    {
        var assets = show.Views.SelectMany(view => view.Assets)
            .GroupBy(asset => asset.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var dto in assetDtos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = assets[dto.Id];
            var entry = archive.CreateEntry(dto.ArchivePath, CompressionLevel.NoCompression);
            using var source = new FileStream(asset.RuntimeSourceReference, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var destination = entry.Open();
            source.CopyTo(destination, 64 * 1024);
        }
    }

    private static bool ValidateAssetStream(
        ZipArchiveEntry entry,
        AssetDtoV1 dto,
        AssetMediaType mediaType,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        FileStream? output = null;
        try
        {
            if (outputPath is not null)
            {
                output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            using var input = entry.Open();
            var buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total = checked(total + read);
                if (total > ShowFileLimits.MaximumAssetBytes)
                {
                    throw new ShowFileException($"Asset '{dto.Id}' exceeds the allowed expanded size.");
                }
                hash.AppendData(buffer, 0, read);
                output?.Write(buffer, 0, read);
            }
            output?.Flush(flushToDisk: true);
            output?.Dispose();
            output = null;
            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var valid = total == dto.ByteLength
                && CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actualHash),
                    Encoding.ASCII.GetBytes(dto.Sha256));
            if (valid && outputPath is not null)
            {
                valid = HasValidMediaSignature(outputPath, mediaType);
            }
            if (!valid && outputPath is not null)
            {
                TryDeleteFile(outputPath);
            }
            return valid;
        }
        finally
        {
            output?.Dispose();
            hash.Dispose();
        }
    }

    private static void ValidateCredentialSafety(ShowDefinition show) => ValidateCredentialSafety(show.Cameras);

    private static void ValidateCredentialSafety(IEnumerable<CameraDefinition> cameras)
    {
        foreach (var camera in cameras)
        {
            if (Uri.TryCreate(camera.RtspUrl, UriKind.Absolute, out var uri)
                && !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new ShowFileException(
                    $"Camera '{camera.Name}' contains credentials in its RTSP URL. Schema v1 refuses to persist plaintext credentials.");
            }
        }
    }

    private static string GetArchiveAssetPath(string assetId, AssetMediaType mediaType)
    {
        var idHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assetId))).ToLowerInvariant();
        return $"assets/{idHash}.{(mediaType == AssetMediaType.Png ? "png" : "jpg")}";
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsLowerHexSha256(string value)
        => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains("\\", StringComparison.Ordinal)
            || path.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(path))
        {
            return false;
        }
        return path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
        => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static AssetMediaType ParseMediaType(string value)
        => value switch
        {
            "image/png" => AssetMediaType.Png,
            "image/jpeg" => AssetMediaType.Jpeg,
            _ => throw new ShowFileException($"Unsupported asset media type '{value}'."),
        };

    private static string FormatMediaType(AssetMediaType value)
        => value switch
        {
            AssetMediaType.Png => "image/png",
            AssetMediaType.Jpeg => "image/jpeg",
            _ => throw new ShowFileException($"Unsupported asset media type '{value}'."),
        };

    private static void ValidateMediaFile(string path, AssetMediaType mediaType)
    {
        if (!HasValidMediaSignature(path, mediaType))
        {
            throw new ShowFileException($"Asset '{Path.GetFileName(path)}' is not a valid {FormatMediaType(mediaType)} file.");
        }
    }

    private static bool HasValidMediaSignature(string path, AssetMediaType mediaType)
    {
        Span<byte> header = stackalloc byte[8];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        return mediaType switch
        {
            AssetMediaType.Png => read >= 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            AssetMediaType.Jpeg => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            _ => false,
        };
    }

    private static TEnum ParseEnum<TEnum>(string? value, string field, string elementId)
        where TEnum : struct, Enum
        => value is not null
           && Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed)
           && Enum.IsDefined(parsed)
            ? parsed
            : throw new ShowFileException($"Scene element '{elementId}' has invalid {field} '{value}'.");

    private static string Required(string? value, string field, string elementId)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ShowFileException($"Scene element '{elementId}' is missing {field}.")
            : value;

    private static void RejectDuplicates<T>(IEnumerable<T> values, Func<T, string> id, string label)
    {
        var duplicate = values.GroupBy(id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ShowFileException($"The manifest contains duplicate {label} ID '{duplicate.Key}'.");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class AtomicFileCommitter : IAtomicFileCommitter
    {
        public void Commit(string temporaryPath, string destinationPath, string backupPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Copy(destinationPath, backupPath, overwrite: true);
                using (var backup = new FileStream(backupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    backup.Flush(flushToDisk: true);
                }
            }
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
    }
}
