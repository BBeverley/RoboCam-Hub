namespace RoboCamHub.NativeInterop;

public readonly record struct NativeAbiVersion(ushort Major, ushort Minor)
{
    public static NativeAbiVersion Supported { get; } = new(1, 10);

    internal static NativeAbiVersion FromEncoded(uint value)
        => new((ushort)(value >> 16), (ushort)value);

    public override string ToString() => $"{Major}.{Minor}";
}
