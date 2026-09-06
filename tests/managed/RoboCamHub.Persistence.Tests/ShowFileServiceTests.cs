using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using RoboCamHub.Domain;
using RoboCamHub.Persistence;

namespace RoboCamHub.Persistence.Tests;

public sealed class ShowFileServiceTests
{
    [Fact]
    public async Task BlankShowRoundTripsWithStableIdentity()
    {
        using var files = new TestFiles();
        var service = files.CreateService();
        var show = new ShowDefinition("show-1", "Blank", [], [new ViewDefinition("view-1", "Main")], []);

        await service.SaveAsync(show, files.ShowPath);
        using var loaded = await service.LoadAsync(files.ShowPath);

        Assert.Equal("show-1", loaded.Show.Id);
        Assert.Equal("Blank", loaded.Show.Name);
        Assert.Equal("view-1", Assert.Single(loaded.Show.Views).Id);
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public async Task RepresentativeShowRoundTripsEveryDurableTypeAndStableId()
    {
        using var files = new TestFiles();
        var show = files.CreateRepresentativeShow();
        var service = files.CreateService();

        await service.SaveAsync(show, files.ShowPath);
        File.Delete(files.PngPath);
        File.Delete(files.JpegPath);
        using var loaded = await service.LoadAsync(files.ShowPath);

        Assert.Equal(show.Cameras.Select(item => item.Id), loaded.Show.Cameras.Select(item => item.Id));
        Assert.Equal(show.Views.Select(item => item.Id), loaded.Show.Views.Select(item => item.Id));
        Assert.Equal(show.Outputs.Select(item => item.Id), loaded.Show.Outputs.Select(item => item.Id));
        Assert.Equal("view-a", Assert.Single(loaded.Show.Outputs).ViewId);
        var view = loaded.Show.Views[0];
        Assert.Equal(show.Views[0].SceneElements.Select(item => item.Id), view.SceneElements.Select(item => item.Id));
        var camera = Assert.IsType<CameraElementDefinition>(view.SceneElements[0]);
        Assert.Equal((-0.1, 0.2, 1.2, 0.6, 0.1, 0.2, 0.15, 0.05, 22.5, true, true, 7, CameraElementFitMode.Cover),
            (camera.X, camera.Y, camera.Width, camera.Height, camera.CropLeft, camera.CropTop,
                camera.CropRight, camera.CropBottom, camera.RotationDegrees, camera.FlipHorizontal,
                camera.FlipVertical, camera.ZOrder, camera.FitMode));
        var text = Assert.IsType<TextElementDefinition>(view.SceneElements[1]);
        Assert.Equal(("Act I ✓", "Inter", 72d, TextElementAlignment.Center, TextElementVerticalAlignment.Bottom,
                TextElementWeight.Bold, TextElementStyle.Italic, true, 0x112233FFU, 0x01020380U),
            (text.Text, text.FontFamily, text.FontSize, text.Alignment, text.VerticalAlignment,
                text.Weight, text.Style, text.Underline, text.TextColorRgba, text.BackgroundColorRgba));
        Assert.Equal(new[] { "asset-jpeg", "asset-png" }, view.Assets.Select(asset => asset.Id).Order());
        Assert.All(view.Assets, asset => Assert.StartsWith(loaded.MaterializedAssetDirectory, asset.RuntimeSourceReference));
        Assert.True(File.Exists(view.Assets.Single(asset => asset.Id == "asset-png").RuntimeSourceReference));
        Assert.IsType<ShapeElementDefinition>(view.SceneElements[4]);
        Assert.IsType<FrameElementDefinition>(view.SceneElements[5]);

        var movedPath = Path.Combine(files.Directory, "moved", "portable.rchshow");
        await service.SaveAsync(loaded.Show, movedPath);
        using var moved = await service.LoadAsync(movedPath);
        Assert.Equal(new[] { "asset-jpeg", "asset-png" }, moved.Show.Views[0].Assets.Select(asset => asset.Id).Order());
    }

    [Fact]
    public async Task PortableManifestExcludesRuntimeAndMachineState()
    {
        using var files = new TestFiles();
        await files.CreateService().SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);

        var manifest = ReadManifest(files.ShowPath);

        Assert.DoesNotContain(files.Directory, manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeSourceReference", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullscreen", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("showMode", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receiver", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("native", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("physicalNic", manifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlaintextCredentialsAreRefused()
    {
        using var files = new TestFiles();
        var show = new ShowDefinition(
            "show", "Credentials",
            [new CameraDefinition("camera", "Camera", "rtsp://user:secret@10.0.0.1/stream")],
            [new ViewDefinition("view", "View")], []);

        var error = await Assert.ThrowsAsync<ShowFileException>(() => files.CreateService().SaveAsync(show, files.ShowPath));

        Assert.Contains("refuses to persist plaintext credentials", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(files.ShowPath));
    }

    [Fact]
    public async Task UnsupportedSchemaMalformedJsonAndTruncatedZipFailClearly()
    {
        using var files = new TestFiles();
        var service = files.CreateService();
        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        RewriteManifest(files.ShowPath, root => root["schemaVersion"] = 2);
        var versionError = await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));
        Assert.Contains("unsupported schema version 2", versionError.Message, StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(files.ShowPath, "not a zip");
        await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));
    }

    [Fact]
    public async Task DuplicateIdsAndInvalidReferencesAreRejected()
    {
        using var files = new TestFiles();
        var service = files.CreateService();
        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        RewriteManifest(files.ShowPath, root =>
        {
            var cameras = root["show"]!["cameras"]!.AsArray();
            cameras.Add(cameras[0]!.DeepClone());
        });
        var duplicate = await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));
        Assert.Contains("duplicate camera ID", duplicate.Message, StringComparison.OrdinalIgnoreCase);

        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        RewriteManifest(files.ShowPath, root => root["show"]!["outputs"]![0]!["viewId"] = "missing");
        var reference = await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));
        Assert.Contains("missing View", reference.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownElementAndIllegalArchivePathAreRejected()
    {
        using var files = new TestFiles();
        var service = files.CreateService();
        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        RewriteManifest(files.ShowPath, root => root["show"]!["views"]![0]!["sceneElements"]![0]!["kind"] = "video");
        var kind = await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));
        Assert.Contains("unknown kind", kind.Message, StringComparison.OrdinalIgnoreCase);

        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        RewriteManifest(files.ShowPath, root => root["show"]!["assets"]![0]!["archivePath"] = "../escape.png");
        var path = await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));
        Assert.Contains("illegal archive path", path.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TraversalAndUndeclaredArchiveEntriesAreRejected()
    {
        using var files = new TestFiles();
        var service = files.CreateService();
        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        AddArchiveEntry(files.ShowPath, "../outside.txt");
        var traversal = await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));
        Assert.Contains("illegal path", traversal.Message, StringComparison.OrdinalIgnoreCase);

        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        AddArchiveEntry(files.ShowPath, "assets/not-declared.png");
        var undeclared = await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));
        Assert.Contains("not declared", undeclared.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidTransformsAndUndeclaredAssetReferencesAreRejected()
    {
        using var files = new TestFiles();
        var service = files.CreateService();
        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        RewriteManifest(files.ShowPath, root =>
            root["show"]!["views"]![0]!["sceneElements"]![0]!["width"] = 0);
        await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));

        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        RewriteManifest(files.ShowPath, root =>
            root["show"]!["views"]![0]!["sceneElements"]![2]!["assetId"] = "not-declared");
        var asset = await Assert.ThrowsAsync<ShowFileException>(() => service.LoadAsync(files.ShowPath));
        Assert.Contains("undeclared asset", asset.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrCorruptAssetLoadsDegradedWithExplicitWarning(bool corrupt)
    {
        using var files = new TestFiles();
        var service = files.CreateService();
        await service.SaveAsync(files.CreateRepresentativeShow(), files.ShowPath);
        RewriteArchive(files.ShowPath, (name, bytes) =>
        {
            if (!name.StartsWith("assets/", StringComparison.Ordinal))
            {
                return bytes;
            }
            return corrupt ? [.. bytes.Select((value, index) => index == bytes.Length - 1 ? (byte)(value ^ 0xFF) : value)] : null;
        }, affectFirstAssetOnly: true);

        using var loaded = await service.LoadAsync(files.ShowPath);

        Assert.NotEmpty(loaded.Warnings);
        Assert.Single(loaded.Show.Views[0].SceneElements.OfType<ImageElementDefinition>());
        Assert.Single(loaded.Show.Views[0].Assets);
    }

    [Fact]
    public async Task AtomicCommitFailurePreservesPreviousGoodFile()
    {
        using var files = new TestFiles();
        var goodService = files.CreateService();
        var original = files.CreateRepresentativeShow("Original");
        await goodService.SaveAsync(original, files.ShowPath);
        var originalBytes = File.ReadAllBytes(files.ShowPath);
        var failingService = new ShowFileService(files.CachePath, new ThrowingCommitter());

        await Assert.ThrowsAsync<ShowFileException>(() => failingService.SaveAsync(
            files.CreateRepresentativeShow("Replacement"), files.ShowPath));

        Assert.Equal(originalBytes, File.ReadAllBytes(files.ShowPath));
        using var loaded = await goodService.LoadAsync(files.ShowPath);
        Assert.Equal("Original", loaded.Show.Name);
    }

    [Fact]
    public async Task SuccessfulReplacementKeepsLastKnownGoodBackup()
    {
        using var files = new TestFiles();
        var service = files.CreateService();
        await service.SaveAsync(files.CreateRepresentativeShow("Original"), files.ShowPath);
        await service.SaveAsync(files.CreateRepresentativeShow("Replacement"), files.ShowPath);

        using var current = await service.LoadAsync(files.ShowPath);
        using var backup = await service.LoadAsync(files.ShowPath + ".bak");

        Assert.Equal("Replacement", current.Show.Name);
        Assert.Equal("Original", backup.Show.Name);
    }

    private static string ReadManifest(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        return reader.ReadToEnd();
    }

    private static void RewriteManifest(string path, Action<JsonObject> mutation)
    {
        RewriteArchive(path, (name, bytes) =>
        {
            if (name != "manifest.json")
            {
                return bytes;
            }
            var root = JsonNode.Parse(bytes)!.AsObject();
            mutation(root);
            return Encoding.UTF8.GetBytes(root.ToJsonString());
        });
    }

    private static void AddArchiveEntry(string path, string name)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.WriteByte(1);
    }

    private static void RewriteArchive(
        string path,
        Func<string, byte[], byte[]?> mutation,
        bool affectFirstAssetOnly = false)
    {
        var entries = new List<(string Name, byte[] Bytes)>();
        using (var source = ZipFile.OpenRead(path))
        {
            foreach (var entry in source.Entries)
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                entries.Add((entry.FullName, memory.ToArray()));
            }
        }
        File.Delete(path);
        var affected = false;
        using var destination = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var shouldMutate = !affectFirstAssetOnly || !affected || !entry.Name.StartsWith("assets/", StringComparison.Ordinal);
            var bytes = shouldMutate ? mutation(entry.Name, entry.Bytes) : entry.Bytes;
            if (entry.Name.StartsWith("assets/", StringComparison.Ordinal) && shouldMutate)
            {
                affected = true;
            }
            if (bytes is null)
            {
                continue;
            }
            var output = destination.CreateEntry(entry.Name, CompressionLevel.NoCompression);
            using var stream = output.Open();
            stream.Write(bytes);
        }
    }

    private sealed class ThrowingCommitter : IAtomicFileCommitter
    {
        public void Commit(string temporaryPath, string destinationPath, string backupPath)
            => throw new IOException("simulated commit failure");
    }

    private sealed class TestFiles : IDisposable
    {
        public TestFiles()
        {
            Directory = Path.Combine(Path.GetTempPath(), $"rch-g6f-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
            CachePath = Path.Combine(Directory, "cache");
            ShowPath = Path.Combine(Directory, "test.rchshow");
            PngPath = Path.Combine(Directory, "logo.png");
            JpegPath = Path.Combine(Directory, "photo.jpg");
            File.WriteAllBytes(PngPath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAHnOcQAAAAABJRU5ErkJggg=="));
            File.WriteAllBytes(JpegPath, [0xFF, 0xD8, 0xFF, 0xD9]);
        }

        public string Directory { get; }
        public string CachePath { get; }
        public string ShowPath { get; }
        public string PngPath { get; }
        public string JpegPath { get; }

        public ShowFileService CreateService() => new(CachePath);

        public ShowDefinition CreateRepresentativeShow(string name = "Representative")
        {
            if (!File.Exists(PngPath))
            {
                File.WriteAllBytes(PngPath, Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAHnOcQAAAAABJRU5ErkJggg=="));
            }
            if (!File.Exists(JpegPath))
            {
                File.WriteAllBytes(JpegPath, [0xFF, 0xD8, 0xFF, 0xD9]);
            }
            var cameras = new[]
            {
                new CameraDefinition("camera-1", "Spot 1", "rtsp://10.0.0.1/profile2/media.smp"),
                new CameraDefinition("camera-2", "Spot 2", "rtsp://10.0.0.2/profile2/media.smp", enabled: false),
            };
            var png = new AssetDefinition("asset-png", "logo.png", AssetMediaType.Png, PngPath, 1, 1);
            var jpeg = new AssetDefinition("asset-jpeg", "photo.jpg", AssetMediaType.Jpeg, JpegPath, 1, 1);
            ViewSceneElementDefinition[] elements =
            [
                new CameraElementDefinition("element-camera", "camera-1", -0.1, 0.2, 1.2, 0.6, 7,
                    0.1, 0.2, 0.15, 0.05, 22.5, true, true, true, true, CameraElementFitMode.Cover),
                new TextElementDefinition("element-text", "Act I ✓", 0.1, 0.1, 0.8, 0.2, 8,
                    "Inter", 72, TextElementAlignment.Center, TextElementWeight.Bold, TextElementStyle.Italic,
                    0x112233FF, 0x01020380, 5, true, false, true, true, TextElementVerticalAlignment.Bottom, true),
                new ImageElementDefinition("element-png", png.Id, 0, 0, 0.2, 0.2, 9,
                    CameraElementFitMode.Contain, 0.75, 10, true),
                new ImageElementDefinition("element-jpeg", jpeg.Id, 0.8, 0.8, 0.2, 0.2, 10,
                    CameraElementFitMode.Stretch, 0.5),
                new ShapeElementDefinition("element-shape", 0, 0.8, 1, 0.2, 2, 0x445566FF,
                    0xFFFFFFFF, 4, 0.8, 2),
                new FrameElementDefinition("element-frame", 0, 0, 1, 1, 20, 0xABCDEF80, 12, 0.9, -3),
            ];
            var views = new[]
            {
                new ViewDefinition("view-a", "Spots A", elements, [png, jpeg]),
                new ViewDefinition("view-b", "Spots B", "camera-2"),
            };
            return new ShowDefinition(
                "show-1", name, cameras, views,
                [new OutputDefinition("output-1", "Program", "ROBOCAM - PROGRAM", "view-a")],
                "view-a");
        }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
