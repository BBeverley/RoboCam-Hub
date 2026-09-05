namespace RoboCamHub.Application.Tests;

public sealed class StatusPollingServiceTests
{
    [Fact]
    public async Task PollingNeverOverlapsRefreshOperations()
    {
        var active = 0;
        var maximumActive = 0;
        var refreshCount = 0;
        await using var polling = new StatusPollingService(
            async cancellationToken =>
            {
                var current = Interlocked.Increment(ref active);
                InterlockedExtensions.Max(ref maximumActive, current);
                Interlocked.Increment(ref refreshCount);
                try
                {
                    await Task.Delay(30, cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            },
            TimeSpan.FromMilliseconds(2));

        polling.Start();
        await WaitUntilAsync(() => Volatile.Read(ref refreshCount) >= 3, TimeSpan.FromSeconds(2));

        Assert.Equal(1, maximumActive);
    }

    [Fact]
    public async Task PollingDisposalStopsFutureRefreshesCleanly()
    {
        var refreshCount = 0;
        var polling = new StatusPollingService(
            _ =>
            {
                Interlocked.Increment(ref refreshCount);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(5));
        polling.Start();
        await WaitUntilAsync(() => Volatile.Read(ref refreshCount) >= 2, TimeSpan.FromSeconds(2));

        await polling.DisposeAsync();
        var countAfterDisposal = Volatile.Read(ref refreshCount);
        await Task.Delay(40);

        Assert.False(polling.IsRunning);
        Assert.Equal(countAfterDisposal, Volatile.Read(ref refreshCount));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!predicate())
        {
            await Task.Delay(5, cancellation.Token);
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (value > current)
            {
                var previous = Interlocked.CompareExchange(ref location, value, current);
                if (previous == current)
                {
                    return;
                }

                current = previous;
            }
        }
    }
}
