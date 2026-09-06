using System.Text.Json;

namespace RoboCamHub.Persistence;

public sealed class MachinePreferences
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "Auto";
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public string? WindowState { get; set; }
    public string? LastFolder { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public Dictionary<string, string> PhysicalNicMappings { get; set; } = new(StringComparer.Ordinal);
    public string? DecoderPreference { get; set; }
    public string? CompositorPreference { get; set; }
}

public sealed class MachinePreferencesStore
{
    private const long MaximumPreferencesBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 16,
    };

    public MachinePreferencesStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(MachineAppDataPaths.Root, "preferences.json");
    }

    public string Path { get; }

    public async Task<MachinePreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return new MachinePreferences();
        }
        var info = new FileInfo(Path);
        if (info.Length > MaximumPreferencesBytes)
        {
            throw new ShowFileException("Machine preferences exceed the allowed size.");
        }
        try
        {
            await using var stream = File.OpenRead(Path);
            var preferences = await JsonSerializer.DeserializeAsync<MachinePreferences>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? new MachinePreferences();
            if (preferences.SchemaVersion != 1)
            {
                throw new ShowFileException(
                    $"Machine preferences schema version {preferences.SchemaVersion} is unsupported.");
            }
            preferences.RecentFiles = preferences.RecentFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
            preferences.PhysicalNicMappings = new Dictionary<string, string>(
                preferences.PhysicalNicMappings,
                StringComparer.Ordinal);
            return preferences;
        }
        catch (ShowFileException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ShowFileException($"Loading machine preferences failed: {exception.Message}", exception);
        }
    }

    public async Task SaveAsync(MachinePreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new ShowFileException("The machine preferences path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = System.IO.Path.Combine(directory, $".preferences.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, Path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}

public static class MachineAppDataPaths
{
    public static string Root => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RoboCam-Hub");
}
