using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RoboCamHub.Application;

namespace RoboCamHub.App;

internal sealed class ViewEditorCanvas : Control
{
    private const double CanvasAspectRatio = 16d / 9d;
    private const double HandleRadius = 6;
    private const double RotationHandleOffset = 24;
    private static readonly IBrush CanvasBrush = new SolidColorBrush(Color.Parse("#070B10"));
    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.Parse("#25313D"));
    private static readonly IBrush ContainerBrush = new SolidColorBrush(Color.Parse("#607080"));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.Parse("#67B7FF"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#F3F7FA"));
    private static readonly IBrush[] ElementBrushes =
    [
        new SolidColorBrush(Color.Parse("#244C68")),
        new SolidColorBrush(Color.Parse("#4B3869")),
        new SolidColorBrush(Color.Parse("#365947")),
        new SolidColorBrush(Color.Parse("#69463A")),
    ];

    private ViewEditorViewModel? _editor;
    private readonly List<ViewEditorElementViewModel> _subscribedElements = [];
    private PointerInteraction _pointerInteraction;

    private enum PointerInteraction
    {
        None,
        Move,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight,
        Rotate,
    }

    public event EventHandler? PropertiesRequested;

    public event EventHandler<string>? LocateSourceRequested;

    public ViewEditorViewModel? Editor
    {
        get => _editor;
        set
        {
            if (ReferenceEquals(_editor, value))
            {
                return;
            }

            Unsubscribe(_editor);
            _editor = value;
            Subscribe(_editor);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var viewport = GetViewport();
        context.DrawRectangle(CanvasBrush, null, viewport);
        var guidePen = new Pen(GuideBrush, 1, dashStyle: DashStyle.Dash);
        context.DrawLine(
            guidePen,
            new Point(viewport.Center.X, viewport.Top),
            new Point(viewport.Center.X, viewport.Bottom));
        context.DrawLine(
            guidePen,
            new Point(viewport.Left, viewport.Center.Y),
            new Point(viewport.Right, viewport.Center.Y));

        if (Editor is null)
        {
            return;
        }

        using (context.PushClip(viewport))
        {
            foreach (var element in Editor.Elements
                         .Where(element => element.IsVisibleOnCanvas)
                         .OrderBy(element => element.ZOrder)
                         .ThenBy(element => element.Id, StringComparer.Ordinal))
            {
                DrawElement(context, viewport, element);
            }

            if (Editor.SelectedElement is { IsVisibleOnCanvas: true } selected)
            {
                DrawSelection(context, viewport, selected);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        var editor = Editor;
        if (editor is null || editor.IsApplying)
        {
            return;
        }

        Focus();
        var pixelPoint = eventArgs.GetPosition(this);
        var current = eventArgs.GetCurrentPoint(this);
        if (current.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            var target = editor.SelectAt(ToNormalized(pixelPoint));
            InvalidateVisual();
            if (target is not null)
            {
                OpenContextMenu();
            }
            eventArgs.Handled = true;
            return;
        }

        if (current.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        _pointerInteraction = HitSelectionHandle(pixelPoint);
        var normalized = ToNormalized(pixelPoint);
        var selected = editor.SelectedElement;
        if (_pointerInteraction != PointerInteraction.None && selected is not null)
        {
            BeginHandleInteraction(editor, selected.Id, normalized, _pointerInteraction);
        }
        else
        {
            selected = editor.SelectAt(normalized);
            _pointerInteraction = selected is null ? PointerInteraction.None : PointerInteraction.Move;
            if (selected is not null)
            {
                editor.BeginMove(selected.Id, normalized);
            }
        }

        if (_pointerInteraction != PointerInteraction.None)
        {
            eventArgs.Pointer.Capture(this);
        }
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        if (_pointerInteraction == PointerInteraction.None || Editor is null)
        {
            return;
        }

        var point = ToNormalized(eventArgs.GetPosition(this));
        var preserveAspect = !eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (_pointerInteraction)
        {
            case PointerInteraction.Move:
                Editor.UpdateMove(point);
                break;
            case PointerInteraction.Rotate:
                Editor.UpdateRotation(point);
                break;
            default:
                Editor.UpdateResize(point, preserveAspect);
                break;
        }
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    protected override async void OnPointerReleased(PointerReleasedEventArgs eventArgs)
    {
        base.OnPointerReleased(eventArgs);
        if (_pointerInteraction == PointerInteraction.None || Editor is null)
        {
            return;
        }

        _pointerInteraction = PointerInteraction.None;
        eventArgs.Pointer.Capture(null);
        await Editor.CommitInteractionAsync();
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs eventArgs)
    {
        base.OnPointerCaptureLost(eventArgs);
        if (_pointerInteraction == PointerInteraction.None)
        {
            return;
        }

        _pointerInteraction = PointerInteraction.None;
        Editor?.CancelInteraction();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        if (Editor is null || Editor.IsApplying)
        {
            return;
        }

        var viewport = GetViewport();
        var multiplier = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        Task<bool>? operation = eventArgs.Key switch
        {
            Key.Left => Editor.NudgeSelectedAsync(-multiplier / viewport.Width, 0),
            Key.Right => Editor.NudgeSelectedAsync(multiplier / viewport.Width, 0),
            Key.Up => Editor.NudgeSelectedAsync(0, -multiplier / viewport.Height),
            Key.Down => Editor.NudgeSelectedAsync(0, multiplier / viewport.Height),
            Key.Delete or Key.Back => Editor.DeleteSelectedAsync(),
            Key.D when eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control)
                       || eventArgs.KeyModifiers.HasFlag(KeyModifiers.Meta)
                => Editor.DuplicateSelectedAsync(),
            _ => null,
        };
        if (operation is not null)
        {
            _ = CompleteKeyboardOperationAsync(operation);
            eventArgs.Handled = true;
        }
    }

    private async Task CompleteKeyboardOperationAsync(Task<bool> operation)
    {
        await operation;
        InvalidateVisual();
    }

    private void DrawElement(
        DrawingContext context,
        Rect viewport,
        ViewEditorElementViewModel element)
    {
        var points = GetElementCorners(viewport, element.Geometry.VisibleCorners);
        var geometry = CreatePolygon(points);
        var fill = ElementBrushes[Math.Abs(StringComparer.Ordinal.GetHashCode(element.CameraId)) % ElementBrushes.Length];
        context.DrawGeometry(fill, new Pen(new SolidColorBrush(Color.Parse("#63829A")), 1), geometry);

        var text = new FormattedText(
            $"{element.CameraName}\nZ {element.ZOrder}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
            13,
            TextBrush);
        var centre = ToPixel(viewport, element.Geometry.VisibleBounds.Centre);
        context.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
    }

    private void DrawSelection(
        DrawingContext context,
        Rect viewport,
        ViewEditorElementViewModel element)
    {
        var geometry = element.Geometry;
        if (geometry.HasTransparentContainerSpace)
        {
            var containerPen = new Pen(ContainerBrush, 1, dashStyle: DashStyle.Dash);
            context.DrawGeometry(
                null,
                containerPen,
                CreatePolygon(GetElementCorners(viewport, geometry.DestinationCorners)));
        }

        var corners = GetElementCorners(viewport, geometry.ManipulationCorners);
        context.DrawGeometry(null, new Pen(SelectionBrush, 2), CreatePolygon(corners));
        foreach (var corner in corners)
        {
            context.DrawEllipse(SelectionBrush, new Pen(Brushes.White, 1), corner, HandleRadius, HandleRadius);
        }

        var rotationHandle = GetRotationHandle(viewport, geometry);
        var topCentre = Midpoint(corners[0], corners[1]);
        context.DrawLine(new Pen(SelectionBrush, 1), topCentre, rotationHandle);
        context.DrawEllipse(SelectionBrush, new Pen(Brushes.White, 1), rotationHandle, HandleRadius, HandleRadius);
    }

    private PointerInteraction HitSelectionHandle(Point point)
    {
        if (Editor?.SelectedElement is not { IsVisibleOnCanvas: true } selected)
        {
            return PointerInteraction.None;
        }

        var viewport = GetViewport();
        var rotation = GetRotationHandle(viewport, selected.Geometry);
        if (Distance(point, rotation) <= HandleRadius + 3)
        {
            return PointerInteraction.Rotate;
        }

        var corners = GetElementCorners(viewport, selected.Geometry.ManipulationCorners);
        var kinds = new[]
        {
            PointerInteraction.ResizeTopLeft,
            PointerInteraction.ResizeTopRight,
            PointerInteraction.ResizeBottomRight,
            PointerInteraction.ResizeBottomLeft,
        };
        for (var index = 0; index < corners.Length; index++)
        {
            if (Distance(point, corners[index]) <= HandleRadius + 3)
            {
                return kinds[index];
            }
        }
        return PointerInteraction.None;
    }

    private static void BeginHandleInteraction(
        ViewEditorViewModel editor,
        string elementId,
        EditorPoint point,
        PointerInteraction interaction)
    {
        switch (interaction)
        {
            case PointerInteraction.Rotate:
                editor.BeginRotate(elementId, point);
                break;
            case PointerInteraction.ResizeTopLeft:
                editor.BeginResize(elementId, EditorResizeCorner.TopLeft, point);
                break;
            case PointerInteraction.ResizeTopRight:
                editor.BeginResize(elementId, EditorResizeCorner.TopRight, point);
                break;
            case PointerInteraction.ResizeBottomLeft:
                editor.BeginResize(elementId, EditorResizeCorner.BottomLeft, point);
                break;
            case PointerInteraction.ResizeBottomRight:
                editor.BeginResize(elementId, EditorResizeCorner.BottomRight, point);
                break;
        }
    }

    private void OpenContextMenu()
    {
        if (Editor?.SelectedElement is not { } selected)
        {
            return;
        }

        var properties = new MenuItem { Header = "Properties…" };
        properties.Click += (_, _) => PropertiesRequested?.Invoke(this, EventArgs.Empty);
        var locate = new MenuItem { Header = "Locate Source" };
        locate.Click += (_, _) => LocateSourceRequested?.Invoke(this, selected.CameraId);
        var duplicate = new MenuItem { Header = "Duplicate" };
        duplicate.Click += async (_, _) => await Editor.DuplicateSelectedAsync();
        var forward = new MenuItem { Header = "Bring Forward" };
        forward.Click += async (_, _) => await Editor.BringForwardAsync();
        var backward = new MenuItem { Header = "Send Backward" };
        backward.Click += async (_, _) => await Editor.SendBackwardAsync();
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += async (_, _) => await Editor.DeleteSelectedAsync();
        new ContextMenu
        {
            Items = { properties, locate, new Separator(), duplicate, forward, backward, new Separator(), delete },
        }.Open(this);
    }

    private void Subscribe(ViewEditorViewModel? editor)
    {
        if (editor is null)
        {
            return;
        }
        editor.PropertyChanged += OnEditorChanged;
        editor.Elements.CollectionChanged += OnElementsChanged;
        SubscribeElements(editor);
    }

    private void Unsubscribe(ViewEditorViewModel? editor)
    {
        if (editor is null)
        {
            return;
        }
        editor.PropertyChanged -= OnEditorChanged;
        editor.Elements.CollectionChanged -= OnElementsChanged;
        foreach (var element in _subscribedElements)
        {
            element.PropertyChanged -= OnElementChanged;
        }
        _subscribedElements.Clear();
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs eventArgs) => InvalidateVisual();

    private void OnElementChanged(object? sender, PropertyChangedEventArgs eventArgs) => InvalidateVisual();

    private void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        foreach (var element in _subscribedElements)
        {
            element.PropertyChanged -= OnElementChanged;
        }
        _subscribedElements.Clear();
        if (Editor is not null)
        {
            SubscribeElements(Editor);
        }
        InvalidateVisual();
    }

    private void SubscribeElements(ViewEditorViewModel editor)
    {
        foreach (var element in editor.Elements)
        {
            element.PropertyChanged += OnElementChanged;
            _subscribedElements.Add(element);
        }
    }

    private Rect GetViewport()
    {
        var width = Math.Max(1, Bounds.Width);
        var height = Math.Max(1, Bounds.Height);
        var targetWidth = Math.Min(width, height * CanvasAspectRatio);
        var targetHeight = targetWidth / CanvasAspectRatio;
        return new Rect((width - targetWidth) / 2, (height - targetHeight) / 2, targetWidth, targetHeight);
    }

    private EditorPoint ToNormalized(Point point)
    {
        var viewport = GetViewport();
        return new EditorPoint(
            (point.X - viewport.X) / viewport.Width,
            (point.Y - viewport.Y) / viewport.Height);
    }

    private static Point[] GetElementCorners(Rect viewport, IReadOnlyList<EditorPoint> corners)
        => corners.Select(point => ToPixel(viewport, point)).ToArray();

    private static Point ToPixel(Rect viewport, EditorPoint point)
        => new(viewport.X + point.X * viewport.Width, viewport.Y + point.Y * viewport.Height);

    private static Point GetRotationHandle(Rect viewport, EditorElementGeometry geometry)
    {
        var corners = GetElementCorners(viewport, geometry.ManipulationCorners);
        var topCentre = Midpoint(corners[0], corners[1]);
        var centre = ToPixel(viewport, geometry.DestinationBounds.Centre);
        var length = Math.Max(1, Distance(topCentre, centre));
        return new Point(
            topCentre.X + (topCentre.X - centre.X) / length * RotationHandleOffset,
            topCentre.Y + (topCentre.Y - centre.Y) / length * RotationHandleOffset);
    }

    private static StreamGeometry CreatePolygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(points[0], true);
        for (var index = 1; index < points.Count; index++)
        {
            context.LineTo(points[index]);
        }
        context.EndFigure(true);
        return geometry;
    }

    private static Point Midpoint(Point left, Point right)
        => new((left.X + right.X) / 2, (left.Y + right.Y) / 2);

    private static double Distance(Point left, Point right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));
}
