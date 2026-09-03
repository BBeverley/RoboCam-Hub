using System.Runtime.InteropServices;

namespace RoboCamHub.NativeInterop;

/// <summary>
/// Provides the managed entry point for the native ABI version query.
/// </summary>
public sealed class NativeAbiVersionQuery : INativeAbiVersionQuery
{
    public NativeAbiVersion GetVersion()
        => NativeAbiVersion.FromEncoded(NativeMethods.GetAbiVersion());
}

internal static partial class NativeMethods
{
    private const string LibraryName = "robocamhub_native";

    [LibraryImport(LibraryName, EntryPoint = "rch_get_abi_version")]
    internal static partial uint GetAbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "rch_engine_create")]
    internal static partial NativeResult EngineCreate(out NativeEngineHandle engine);

    [LibraryImport(LibraryName, EntryPoint = "rch_engine_destroy")]
    internal static partial NativeResult EngineDestroy(nint engine);
}
