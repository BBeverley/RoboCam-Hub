using System.Runtime.InteropServices;

namespace RoboCamHub.NativeInterop;

/// <summary>
/// Provides the managed entry point for the native ABI version query.
/// </summary>
public sealed class NativeAbiVersionQuery : INativeAbiVersionQuery
{
    public uint GetVersion() => NativeMethods.GetAbiVersion();
}

internal static partial class NativeMethods
{
    private const string LibraryName = "robocamhub_native";

    [LibraryImport(LibraryName, EntryPoint = "rch_get_abi_version")]
    internal static partial uint GetAbiVersion();
}
