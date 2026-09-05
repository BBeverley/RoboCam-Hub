using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime;

public sealed class RuntimeOperationException : InvalidOperationException
{
    internal RuntimeOperationException(string operation, NativeResult result)
        : base($"{operation} failed with native result {result} ({(int)result}).")
    {
        Operation = operation;
        ResultCode = (int)result;
        ResultName = result.ToString();
    }

    public string Operation { get; }

    public int ResultCode { get; }

    public string ResultName { get; }
}
