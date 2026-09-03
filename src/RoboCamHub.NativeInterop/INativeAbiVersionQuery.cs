namespace RoboCamHub.NativeInterop;

/// <summary>
/// Queries the version of the native C ABI supported by the loaded media core.
/// </summary>
public interface INativeAbiVersionQuery
{
    NativeAbiVersion GetVersion();
}
