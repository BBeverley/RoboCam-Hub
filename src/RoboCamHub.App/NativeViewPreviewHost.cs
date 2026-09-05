using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using RoboCamHub.Application;
using RoboCamHub.Runtime;

namespace RoboCamHub.App;

internal sealed class NativeViewPreviewHost : NativeControlHost
{
    private IPlatformHandle? _hostControl;
    private ViewPreviewViewModel? _preview;
    private bool _previewAttached;

    public ViewPreviewViewModel? Preview
    {
        get => _preview;
        set
        {
            if (ReferenceEquals(_preview, value))
            {
                return;
            }
            DetachPreview();
            _preview = value;
            AttachIfReady();
        }
    }

    public void DetachPreview()
    {
        if (_previewAttached)
        {
            _preview?.Detach();
            _previewAttached = false;
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _hostControl = base.CreateNativeControlCore(parent);
        AttachIfReady();
        return _hostControl;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        DetachPreview();
        _hostControl = null;
        base.DestroyNativeControlCore(control);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        AttachIfReady();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        DetachPreview();
        base.OnDetachedFromVisualTree(eventArgs);
    }

    private void AttachIfReady()
    {
        if (_previewAttached || _preview is null || _hostControl is null)
        {
            return;
        }

        var platform = _hostControl.HandleDescriptor switch
        {
            "HWND" => PreviewHostPlatform.WindowsHwnd,
            "NSView" => PreviewHostPlatform.MacOSNsView,
            _ => throw new PlatformNotSupportedException(
                $"Native preview does not support '{_hostControl.HandleDescriptor}'."),
        };
        var handle = unchecked((ulong)_hostControl.Handle.ToInt64());
        _preview.Attach(new PreviewHostSurface(platform, handle, 30));
        _previewAttached = _preview.Attached;
    }
}
