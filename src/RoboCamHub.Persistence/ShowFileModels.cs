using RoboCamHub.Domain;

namespace RoboCamHub.Persistence;

public sealed class ShowFileException : Exception
{
    public ShowFileException(string message)
        : base(message)
    {
    }

    public ShowFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record ShowLoadWarning(string Code, string Message);

public sealed class ShowLoadResult : IDisposable
{
    private string? _materializedAssetDirectory;

    internal ShowLoadResult(
        ShowDefinition show,
        IReadOnlyList<ShowLoadWarning> warnings,
        string materializedAssetDirectory)
    {
        Show = show;
        Warnings = warnings;
        _materializedAssetDirectory = materializedAssetDirectory;
    }

    public ShowDefinition Show { get; }

    public IReadOnlyList<ShowLoadWarning> Warnings { get; }

    public string MaterializedAssetDirectory => _materializedAssetDirectory ?? string.Empty;

    public void Dispose()
    {
        var directory = Interlocked.Exchange(ref _materializedAssetDirectory, null);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public static class ShowFileLimits
{
    public const long MaximumManifestBytes = 4L * 1024 * 1024;
    public const long MaximumAssetBytes = 64L * 1024 * 1024;
    public const long MaximumExpandedArchiveBytes = 512L * 1024 * 1024;
    public const int MaximumArchiveEntries = 300;
    public const double MaximumCompressionRatio = 200;
}

public interface IAtomicFileCommitter
{
    void Commit(string temporaryPath, string destinationPath, string backupPath);
}
