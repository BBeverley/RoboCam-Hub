namespace RoboCamHub.Runtime;

public sealed class RuntimeReferenceException : InvalidOperationException
{
    internal RuntimeReferenceException(string message)
        : base(message)
    {
    }
}
