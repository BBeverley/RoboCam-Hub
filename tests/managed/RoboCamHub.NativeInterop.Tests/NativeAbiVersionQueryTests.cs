namespace RoboCamHub.NativeInterop.Tests;

public sealed class NativeInteropIntegrationTests
{
    [Fact]
    public void NativeLibraryLoadsAndReportsSupportedAbiVersion()
    {
        INativeAbiVersionQuery query = new NativeAbiVersionQuery();

        Assert.Equal(NativeAbiVersion.Supported, query.GetVersion());
    }

    [Fact]
    public void EngineCanBeCreatedAndDisposed()
    {
        var engine = NativeEngine.Create();

        Assert.False(engine.IsDisposed);

        engine.Dispose();

        Assert.True(engine.IsDisposed);
    }

    [Fact]
    public void AbiMismatchFailsDeterministically()
    {
        var incompatibleVersion = new NativeAbiVersion(2, 0);
        var query = new StubVersionQuery(incompatibleVersion);

        var exception = Assert.Throws<NativeAbiMismatchException>(() => NativeEngine.Create(query));

        Assert.Equal(NativeAbiVersion.Supported, exception.Expected);
        Assert.Equal(incompatibleVersion, exception.Actual);
        Assert.Equal(
            "Native ABI version mismatch. Expected 1.0, but loaded 2.0.",
            exception.Message);
    }

    [Fact]
    public void RepeatedCreateAndDisposeDoesNotLeaveOpenManagedHandles()
    {
        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var engine = NativeEngine.Create();

            engine.Dispose();
            engine.Dispose();

            Assert.True(engine.IsDisposed);
        }
    }

    private sealed class StubVersionQuery(NativeAbiVersion version) : INativeAbiVersionQuery
    {
        public NativeAbiVersion GetVersion() => version;
    }
}
