using System.Threading;

namespace RoboCamHub.NativeInterop;

public sealed class NativeEngine : IDisposable
{
    private NativeEngineHandle? _handle;

    private NativeEngine(NativeEngineHandle handle)
    {
        _handle = handle;
    }

    public bool IsDisposed => Volatile.Read(ref _handle) is null;

    public static NativeEngine Create() => Create(new NativeAbiVersionQuery());

    internal static NativeEngine Create(INativeAbiVersionQuery versionQuery)
    {
        ArgumentNullException.ThrowIfNull(versionQuery);

        var actualVersion = versionQuery.GetVersion();
        if (actualVersion != NativeAbiVersion.Supported)
        {
            throw new NativeAbiMismatchException(NativeAbiVersion.Supported, actualVersion);
        }

        var result = NativeMethods.EngineCreate(out var handle);
        if (result != NativeResult.Ok)
        {
            handle?.Dispose();
            throw new InvalidOperationException(
                $"Native engine creation failed with result {result} ({(int)result}).");
        }

        if (handle is null || handle.IsInvalid)
        {
            handle?.Dispose();
            throw new InvalidOperationException("Native engine creation returned an invalid handle.");
        }

        return new NativeEngine(handle);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
        GC.SuppressFinalize(this);
    }
}
