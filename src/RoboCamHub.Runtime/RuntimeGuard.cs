using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime;

internal static class RuntimeGuard
{
    public static void EnsureSuccess(string operation, NativeResult result)
    {
        if (result != NativeResult.Ok)
        {
            throw new RuntimeOperationException(operation, result);
        }
    }
}
