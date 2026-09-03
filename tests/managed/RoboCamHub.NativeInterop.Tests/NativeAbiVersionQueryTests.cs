namespace RoboCamHub.NativeInterop.Tests;

public sealed class NativeAbiVersionQueryTests
{
    [Fact]
    public void NativeVersionQueryImplementsManagedBoundary()
    {
        INativeAbiVersionQuery query = new NativeAbiVersionQuery();

        Assert.IsType<NativeAbiVersionQuery>(query);
    }
}
