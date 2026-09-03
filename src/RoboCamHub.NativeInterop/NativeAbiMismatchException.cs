namespace RoboCamHub.NativeInterop;

public sealed class NativeAbiMismatchException : InvalidOperationException
{
    internal NativeAbiMismatchException(NativeAbiVersion expected, NativeAbiVersion actual)
        : base($"Native ABI version mismatch. Expected {expected}, but loaded {actual}.")
    {
        Expected = expected;
        Actual = actual;
    }

    public NativeAbiVersion Expected { get; }

    public NativeAbiVersion Actual { get; }
}
