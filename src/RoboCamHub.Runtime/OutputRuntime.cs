using RoboCamHub.Domain;
using RoboCamHub.NativeInterop;

namespace RoboCamHub.Runtime;

public sealed class OutputRuntime : IDisposable
{
    private readonly ShowRuntime _owner;
    private readonly INativeRuntimeSender _nativeSender;
    private bool _disposed;

    internal OutputRuntime(
        ShowRuntime owner,
        INativeRuntimeSender nativeSender,
        OutputDefinition definition,
        ViewRuntime view)
    {
        _owner = owner;
        _nativeSender = nativeSender;
        Definition = definition;
        View = view;
    }

    public OutputDefinition Definition { get; }

    public ViewRuntime View { get; }

    public bool IsDisposed => _disposed;

    public void Start()
    {
        ThrowIfUnavailable();
        if (!Definition.Enabled)
        {
            throw new InvalidOperationException(
                $"Output '{Definition.Id}' is disabled by its definition and cannot be started.");
        }

        RuntimeGuard.EnsureSuccess(
            $"Starting Output '{Definition.Id}'",
            _nativeSender.Start());
    }

    public void Stop()
    {
        ThrowIfUnavailable();
        RuntimeGuard.EnsureSuccess(
            $"Stopping Output '{Definition.Id}'",
            _nativeSender.Stop());
    }

    public OutputRuntimeStatus GetStatus()
    {
        ThrowIfUnavailable();
        RuntimeGuard.EnsureSuccess(
            $"Querying Output '{Definition.Id}' status",
            _nativeSender.TryGetStatus(out var status));
        return new OutputRuntimeStatus(
            MapState(status.State),
            status.LastResult.ToString(),
            status.SenderName,
            status.ConfiguredWidth,
            status.ConfiguredHeight,
            status.TargetFps,
            status.SendFpsMilli,
            status.SentFrameCount,
            status.LatestSentSequence,
            status.LatestSentFrameAgeMs,
            status.DroppedOrSkippedFrameCount,
            status.AverageSendDurationUs,
            status.P95SendDurationUs,
            status.ReceiverCountKnown,
            status.ReceiverCount);
    }

    public void Dispose()
    {
        _owner.DisposeOutput(this);
        GC.SuppressFinalize(this);
    }

    internal void DisposeOwned()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = _nativeSender.Stop();
        _nativeSender.Dispose();
    }

    private void ThrowIfUnavailable()
    {
        _owner.ThrowIfDisposed();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static OutputRuntimeState MapState(NativeNdiSenderState state)
        => state switch
        {
            NativeNdiSenderState.Stopped => OutputRuntimeState.Stopped,
            NativeNdiSenderState.Starting => OutputRuntimeState.Starting,
            NativeNdiSenderState.Running => OutputRuntimeState.Running,
            NativeNdiSenderState.WaitingForViewFrame => OutputRuntimeState.WaitingForViewFrame,
            NativeNdiSenderState.Failed => OutputRuntimeState.Failed,
            _ => throw new InvalidOperationException($"Unknown native Output state {(uint)state}."),
        };
}
