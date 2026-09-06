using RoboCamHub.Domain;
using RoboCamHub.Persistence;

namespace RoboCamHub.Persistence.Tests;

public sealed class MachinePreferencesAndRecoveryTests
{
    [Fact]
    public async Task MachinePreferencesRoundTripSeparatelyFromShows()
    {
        using var files = new TempDirectory();
        var store = new MachinePreferencesStore(Path.Combine(files.Path, "preferences.json"));
        var preferences = new MachinePreferences
        {
            Theme = "Dark",
            WindowX = 10,
            WindowY = 20,
            WindowWidth = 1440,
            WindowHeight = 900,
            LastFolder = "/machine/shows",
            RecentFiles = ["/machine/a.rchshow", "/machine/b.rchshow"],
            PhysicalNicMappings = new Dictionary<string, string> { ["NDI Network A"] = "physical-nic-id" },
        };

        await store.SaveAsync(preferences);
        var loaded = await store.LoadAsync();

        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal(1440, loaded.WindowWidth);
        Assert.Equal("physical-nic-id", loaded.PhysicalNicMappings["NDI Network A"]);
    }

    [Fact]
    public async Task RecoveryIsDetectedLoadedAndDiscardedWithoutTouchingMainFile()
    {
        using var files = new TempDirectory();
        var showFiles = new ShowFileService(Path.Combine(files.Path, "cache"));
        var recovery = new RecoveryStore(showFiles, Path.Combine(files.Path, "recovery"));
        var mainPath = Path.Combine(files.Path, "main.rchshow");
        var saved = Show("Saved");
        await showFiles.SaveAsync(saved, mainPath);
        File.SetLastWriteTimeUtc(mainPath, DateTime.UtcNow.AddMinutes(-2));
        var entry = await recovery.SaveAsync(Show("Recovered"), mainPath, DateTimeOffset.UtcNow.AddMinutes(-2));

        var found = Assert.Single(await recovery.FindNewerAsync());
        using var loaded = await recovery.LoadAsync(found);

        Assert.Equal("Recovered", loaded.Show.Name);
        using var main = await showFiles.LoadAsync(mainPath);
        Assert.Equal("Saved", main.Show.Name);
        await recovery.DiscardAsync(entry);
        Assert.Empty(await recovery.FindNewerAsync());
        Assert.True(File.Exists(mainPath));
    }

    [Fact]
    public async Task UnsavedNewShowRecoveryIsDetectedAndCorruptionFailsSafely()
    {
        using var files = new TempDirectory();
        var showFiles = new ShowFileService(Path.Combine(files.Path, "cache"));
        var recovery = new RecoveryStore(showFiles, Path.Combine(files.Path, "recovery"));
        var entry = await recovery.SaveAsync(Show("Unsaved"), null, DateTimeOffset.MinValue);

        Assert.Single(await recovery.FindNewerAsync());
        File.WriteAllText(entry.RecoveryPath, "corrupt");

        await Assert.ThrowsAsync<ShowFileException>(() => recovery.LoadAsync(entry));
        Assert.Single(await recovery.FindNewerAsync());
    }

    private static ShowDefinition Show(string name)
        => new("show", name, [], [new ViewDefinition("view", "View")], []);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rch-g6f-pref-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
