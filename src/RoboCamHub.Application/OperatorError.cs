using System.Diagnostics;
using RoboCamHub.Runtime;

namespace RoboCamHub.Application;

internal static class OperatorError
{
    public static string ForAction(string subject, string action, Exception exception)
    {
        Trace.TraceError("{0} {1} failed: {2}", subject, action, exception);
        return exception switch
        {
            RuntimeOperationException runtime => $"{subject} {action} failed ({runtime.ResultName}).",
            RuntimeReferenceException => $"{subject} is no longer available.",
            ArgumentException or InvalidOperationException => exception.Message,
            _ => $"{subject} {action} failed.",
        };
    }
}
