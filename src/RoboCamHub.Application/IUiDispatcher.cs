namespace RoboCamHub.Application;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
        return Task.CompletedTask;
    }
}
