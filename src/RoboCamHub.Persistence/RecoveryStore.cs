using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RoboCamHub.Domain;

namespace RoboCamHub.Persistence;

public sealed record RecoveryEntry(
    string Key,
    string RecoveryPath,
    string? SourcePath,
    string ShowId,
    DateTimeOffset LastNormalSaveUtc,
    DateTimeOffset RecoveryUtc);

public sealed class RecoveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 8,
    };

    private readonly ShowFileService _showFiles;
    private readonly string _root;

    public RecoveryStore(ShowFileService showFiles, string? root = null)
    {
        _showFiles = showFiles ?? throw new ArgumentNullException(nameof(showFiles));
        _root = Path.GetFullPath(root ?? Path.Combine(MachineAppDataPaths.Root, "Recovery"));
    }

    public async Task<RecoveryEntry> SaveAsync(
        ShowDefinition show,
        string? sourcePath,
        DateTimeOffset lastNormalSaveUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(show);
        Directory.CreateDirectory(_root);
        var normalizedSource = string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetFullPath(sourcePath);
        var key = CreateKey(normalizedSource is null ? $"unsaved:{show.Id}" : $"file:{normalizedSource}");
        var recoveryPath = Path.Combine(_root, $"{key}.rchshow");
        await _showFiles.SaveAsync(show, recoveryPath, cancellationToken).ConfigureAwait(false);
        var entry = new RecoveryEntry(
            key,
            recoveryPath,
            normalizedSource,
            show.Id,
            lastNormalSaveUtc,
            DateTimeOffset.UtcNow);
        await WriteMetadataAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public async Task<IReadOnlyList<RecoveryEntry>> FindNewerAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }
        var entries = new List<RecoveryEntry>();
        foreach (var metadataPath in Directory.EnumerateFiles(_root, "*.recovery.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(metadataPath);
                if (info.Length > 64 * 1024)
                {
                    continue;
                }
                await using var stream = File.OpenRead(metadataPath);
                var entry = await JsonSerializer.DeserializeAsync<RecoveryEntry>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (entry is null
                    || !string.Equals(CreateMetadataPath(entry.Key), metadataPath, StringComparison.Ordinal)
                    || !string.Equals(Path.GetFullPath(entry.RecoveryPath), Path.Combine(_root, $"{entry.Key}.rchshow"), StringComparison.Ordinal)
                    || !File.Exists(entry.RecoveryPath))
                {
                    continue;
                }
                var mainWriteUtc = entry.SourcePath is not null && File.Exists(entry.SourcePath)
                    ? File.GetLastWriteTimeUtc(entry.SourcePath)
                    : DateTime.MinValue;
                if (entry.SourcePath is null || entry.RecoveryUtc.UtcDateTime > mainWriteUtc)
                {
                    entries.Add(entry);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // A damaged machine-local recovery descriptor must never prevent startup.
            }
        }
        return entries.OrderByDescending(entry => entry.RecoveryUtc).ToArray();
    }

    public Task<ShowLoadResult> LoadAsync(RecoveryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _showFiles.LoadAsync(entry.RecoveryPath, cancellationToken);
    }

    public Task DiscardAsync(RecoveryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Task.Run(() =>
        {
            File.Delete(entry.RecoveryPath);
            File.Delete(entry.RecoveryPath + ".bak");
            File.Delete(CreateMetadataPath(entry.Key));
        });
    }

    private async Task WriteMetadataAsync(RecoveryEntry entry, CancellationToken cancellationToken)
    {
        var destination = CreateMetadataPath(entry.Key);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private string CreateMetadataPath(string key) => Path.Combine(_root, $"{key}.recovery.json");

    private static string CreateKey(string identity)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
}
