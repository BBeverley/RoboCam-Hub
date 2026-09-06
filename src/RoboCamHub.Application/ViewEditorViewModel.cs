using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public sealed class ViewEditorViewModel : ObservableObject, IDisposable
{
    public const double MinimumElementSize = 1d / 60d;
    public const double SnapTolerance = 1d / 240d;

    private enum InteractionKind
    {
        None,
        Move,
        Resize,
        Rotate,
    }

    private readonly record struct Interaction(
        InteractionKind Kind,
        EditorPoint StartPointer,
        CameraElementDefinition StartDefinition,
        EditorElementGeometry StartGeometry,
        EditorResizeCorner ResizeCorner);

    private IWorkspaceRuntimeService? _runtime;
    private readonly IUiDispatcher _dispatcher;
    private readonly IReadOnlyList<CameraItemViewModel> _cameras;
    private readonly Action<ViewDefinition> _definitionApplied;
    private IReadOnlyList<ViewSceneElementDefinition> _appliedScene;
    private ViewEditorElementViewModel? _selectedElement;
    private CameraElementPropertiesViewModel? _activeProperties;
    private Interaction _interaction;
    private bool _isApplying;
    private string? _operatorMessage;

    internal ViewEditorViewModel(
        ViewDefinition definition,
        IReadOnlyList<CameraItemViewModel> cameras,
        IWorkspaceRuntimeService runtime,
        IUiDispatcher dispatcher,
        Action<ViewDefinition> definitionApplied)
    {
        ViewId = definition.Id;
        ViewName = definition.Name;
        _cameras = cameras;
        _runtime = runtime;
        _dispatcher = dispatcher;
        _definitionApplied = definitionApplied;
        _appliedScene = [.. definition.SceneElements];
        Elements = [];
        foreach (var camera in _cameras)
        {
            camera.PropertyChanged += OnCameraPropertyChanged;
        }
        if (_cameras is INotifyCollectionChanged observableCameras)
        {
            observableCameras.CollectionChanged += OnCamerasChanged;
        }
        RebuildElements(null);
    }

    public string ViewId { get; }

    public string ViewName { get; }

    public ObservableCollection<ViewEditorElementViewModel> Elements { get; }

    public ViewEditorElementViewModel? SelectedElement
    {
        get => _selectedElement;
        private set
        {
            if (ReferenceEquals(_selectedElement, value))
            {
                return;
            }

            if (_selectedElement is not null)
            {
                _selectedElement.IsSelected = false;
            }
            _selectedElement = value;
            if (_selectedElement is not null)
            {
                _selectedElement.IsSelected = true;
            }
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(HasSelection));
            RaiseCommandState();
        }
    }

    public bool HasSelection => SelectedElement is not null;

    public bool HasPendingTransform => _interaction.Kind != InteractionKind.None;

    public bool HasPendingProperties => ActiveProperties is not null;

    public bool IsApplying
    {
        get => _isApplying;
        private set
        {
            if (SetProperty(ref _isApplying, value))
            {
                RaiseCommandState();
            }
        }
    }

    public CameraElementPropertiesViewModel? ActiveProperties
    {
        get => _activeProperties;
        private set
        {
            if (SetProperty(ref _activeProperties, value))
            {
                RaisePropertyChanged(nameof(HasPendingProperties));
            }
        }
    }

    public string? OperatorMessage
    {
        get => _operatorMessage;
        private set
        {
            if (SetProperty(ref _operatorMessage, value))
            {
                RaisePropertyChanged(nameof(HasOperatorMessage));
            }
        }
    }

    public bool HasOperatorMessage => !string.IsNullOrWhiteSpace(OperatorMessage);

    public ViewEditorElementViewModel? HitTest(EditorPoint point)
    {
        if (!point.IsFinite)
        {
            return null;
        }

        return Elements
            .Where(element => element.IsVisibleOnCanvas && element.Geometry.ContainsVisible(point))
            .OrderByDescending(element => element.ZOrder)
            .ThenByDescending(element => element.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public ViewEditorElementViewModel? SelectAt(EditorPoint point)
    {
        SelectedElement = HitTest(point);
        return SelectedElement;
    }

    public bool SelectElement(string elementId)
    {
        var element = Elements.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        SelectedElement = element;
        return element is not null;
    }

    public void ClearSelection()
    {
        CancelInteraction();
        CancelProperties();
        SelectedElement = null;
    }

    public bool BeginMove(string elementId, EditorPoint pointer)
        => BeginInteraction(elementId, pointer, InteractionKind.Move, EditorResizeCorner.BottomRight);

    public bool BeginResize(string elementId, EditorResizeCorner corner, EditorPoint pointer)
        => BeginInteraction(elementId, pointer, InteractionKind.Resize, corner);

    public bool BeginRotate(string elementId, EditorPoint pointer)
        => BeginInteraction(elementId, pointer, InteractionKind.Rotate, EditorResizeCorner.BottomRight);

    public void UpdateMove(EditorPoint pointer, bool snap = true)
    {
        if (_interaction.Kind != InteractionKind.Move || !pointer.IsFinite || SelectedElement is null)
        {
            return;
        }

        var start = _interaction.StartDefinition;
        var nextX = start.X + pointer.X - _interaction.StartPointer.X;
        var nextY = start.Y + pointer.Y - _interaction.StartPointer.Y;
        if (snap)
        {
            (nextX, nextY) = SnapPosition(start, nextX, nextY);
        }
        nextX = ClampCoordinate(nextX);
        nextY = ClampCoordinate(nextY);
        SelectedElement.Definition = Copy(start, x: nextX, y: nextY);
        RaisePendingChanged();
    }

    public void UpdateResize(EditorPoint pointer, bool preserveAspectRatio = true, bool snap = true)
    {
        if (_interaction.Kind != InteractionKind.Resize || !pointer.IsFinite || SelectedElement is null)
        {
            return;
        }

        var start = _interaction.StartDefinition;
        var pointerX = snap ? SnapCoordinate(pointer.X, false, start.Id) : pointer.X;
        var pointerY = snap ? SnapCoordinate(pointer.Y, true, start.Id) : pointer.Y;
        var horizontalSign = _interaction.ResizeCorner is EditorResizeCorner.TopLeft or EditorResizeCorner.BottomLeft
            ? -1d
            : 1d;
        var verticalSign = _interaction.ResizeCorner is EditorResizeCorner.TopLeft or EditorResizeCorner.TopRight
            ? -1d
            : 1d;
        var radians = start.RotationDegrees * Math.PI / 180;
        var axisX = new EditorPoint(Math.Cos(radians), Math.Sin(radians));
        var axisY = new EditorPoint(-Math.Sin(radians), Math.Cos(radians));
        var pointerDeltaX = (pointerX - _interaction.StartPointer.X) * ViewEditorGeometry.CanvasAspectRatio;
        var pointerDeltaY = pointerY - _interaction.StartPointer.Y;
        var localDeltaX = pointerDeltaX * axisX.X + pointerDeltaY * axisX.Y;
        var localDeltaY = pointerDeltaX * axisY.X + pointerDeltaY * axisY.Y;
        var startVisible = _interaction.StartGeometry.VisibleBounds;
        double width;
        double height;

        if (preserveAspectRatio || start.FitMode == CameraElementFitMode.Contain)
        {
            var proposedVisibleWidth = startVisible.Width * ViewEditorGeometry.CanvasAspectRatio
                                       + horizontalSign * localDeltaX;
            var proposedVisibleHeight = startVisible.Height + verticalSign * localDeltaY;
            var scale = Math.Max(
                proposedVisibleWidth / (startVisible.Width * ViewEditorGeometry.CanvasAspectRatio),
                proposedVisibleHeight / startVisible.Height);
            var minimumScale = Math.Max(MinimumElementSize / start.Width, MinimumElementSize / start.Height);
            var maximumScale = Math.Min(
                ViewSceneElementDefinition.MaximumNormalizedMagnitude / start.Width,
                ViewSceneElementDefinition.MaximumNormalizedMagnitude / start.Height);
            scale = Math.Clamp(scale, minimumScale, maximumScale);
            width = start.Width * scale;
            height = start.Height * scale;
        }
        else
        {
            var visibleWidthFraction = startVisible.Width / start.Width;
            var visibleHeightFraction = startVisible.Height / start.Height;
            width = Math.Clamp(
                start.Width + horizontalSign * localDeltaX
                / ViewEditorGeometry.CanvasAspectRatio / visibleWidthFraction,
                MinimumElementSize,
                ViewSceneElementDefinition.MaximumNormalizedMagnitude);
            height = Math.Clamp(
                start.Height + verticalSign * localDeltaY / visibleHeightFraction,
                MinimumElementSize,
                ViewSceneElementDefinition.MaximumNormalizedMagnitude);
        }

        var sized = Copy(start, width: width, height: height);
        var camera = FindCamera(start.CameraId);
        var sizedGeometry = ViewEditorGeometry.Calculate(
            sized,
            camera?.LatestFrameWidth ?? 0,
            camera?.LatestFrameHeight ?? 0);
        var opposite = OppositeCorner(_interaction.StartGeometry.VisibleCorners, _interaction.ResizeCorner);
        var visibleHalfWidth = sizedGeometry.VisibleBounds.Width * ViewEditorGeometry.CanvasAspectRatio / 2;
        var visibleHalfHeight = sizedGeometry.VisibleBounds.Height / 2;
        var centreX = opposite.X * ViewEditorGeometry.CanvasAspectRatio
                      + horizontalSign * visibleHalfWidth * axisX.X
                      + verticalSign * visibleHalfHeight * axisY.X;
        var centreY = opposite.Y
                      + horizontalSign * visibleHalfWidth * axisX.Y
                      + verticalSign * visibleHalfHeight * axisY.Y;
        var x = ClampCoordinate(centreX / ViewEditorGeometry.CanvasAspectRatio - width / 2);
        var y = ClampCoordinate(centreY - height / 2);
        SelectedElement.Definition = Copy(start, x: x, y: y, width: width, height: height);
        RaisePendingChanged();
    }

    public void UpdateRotation(EditorPoint pointer)
    {
        if (_interaction.Kind != InteractionKind.Rotate || !pointer.IsFinite || SelectedElement is null)
        {
            return;
        }

        var start = _interaction.StartDefinition;
        var centre = new EditorPoint(start.X + start.Width / 2, start.Y + start.Height / 2);
        var startAngle = Math.Atan2(
            _interaction.StartPointer.Y - centre.Y,
            (_interaction.StartPointer.X - centre.X) * ViewEditorGeometry.CanvasAspectRatio);
        var nextAngle = Math.Atan2(
            pointer.Y - centre.Y,
            (pointer.X - centre.X) * ViewEditorGeometry.CanvasAspectRatio);
        var degrees = start.RotationDegrees + (nextAngle - startAngle) * 180 / Math.PI;
        while (degrees > 360)
        {
            degrees -= 360;
        }
        while (degrees < -360)
        {
            degrees += 360;
        }
        SelectedElement.Definition = Copy(start, rotationDegrees: degrees);
        RaisePendingChanged();
    }

    public Task<bool> CommitInteractionAsync()
    {
        if (_interaction.Kind == InteractionKind.None || SelectedElement is null)
        {
            return Task.FromResult(false);
        }

        var selectedId = SelectedElement.Id;
        var pendingDefinition = SelectedElement.Definition;
        var isUnchanged = DefinitionsEqual(_interaction.StartDefinition, pendingDefinition);
        _interaction = default;
        RaisePendingChanged();
        if (isUnchanged)
        {
            return Task.FromResult(true);
        }

        var candidate = ReplaceElement(_appliedScene, pendingDefinition);
        return ApplyCandidateAsync(candidate, selectedId, closePropertiesOnSuccess: false);
    }

    public void CancelInteraction()
    {
        if (_interaction.Kind == InteractionKind.None)
        {
            return;
        }

        var selectedId = SelectedElement?.Id;
        _interaction = default;
        RebuildElements(selectedId);
        RaisePendingChanged();
    }

    public CameraElementPropertiesViewModel? BeginProperties()
    {
        if (SelectedElement is null)
        {
            return null;
        }

        ActiveProperties = new CameraElementPropertiesViewModel(GetAppliedElement(SelectedElement.Id));
        OperatorMessage = null;
        return ActiveProperties;
    }

    public void CancelProperties() => ActiveProperties = null;

    public async Task<bool> ApplyPropertiesAsync()
    {
        var properties = ActiveProperties;
        if (properties is null)
        {
            return false;
        }

        try
        {
            var applied = GetAppliedElement(properties.ElementId);
            var candidate = ReplaceElement(_appliedScene, properties.ToDefinition(applied));
            return await ApplyCandidateAsync(candidate, properties.ElementId, closePropertiesOnSuccess: true)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(() => OperatorMessage = OperatorError.ForAction("Element properties", "apply", exception))
                .ConfigureAwait(false);
            return false;
        }
    }

    public Task<bool> AddCameraAsync(string cameraId)
    {
        var camera = _cameras.FirstOrDefault(item => string.Equals(item.Definition.Id, cameraId, StringComparison.Ordinal));
        if (camera is null)
        {
            return Task.FromResult(false);
        }

        var nextZ = _appliedScene.OfType<CameraElementDefinition>().Select(element => element.ZOrder).DefaultIfEmpty(-1).Max();
        nextZ = Math.Min(1_000_000, nextZ + 1);
        var offset = Math.Min(0.16, _appliedScene.Count * 0.025);
        var element = new CameraElementDefinition(
            $"camera-element-{Guid.NewGuid():N}",
            cameraId,
            0.25 + offset,
            0.25 + offset,
            0.5,
            0.5,
            nextZ,
            fitMode: CameraElementFitMode.Contain);
        return ApplyCandidateAsync([.. _appliedScene, element], element.Id, closePropertiesOnSuccess: false);
    }

    public Task<bool> DuplicateSelectedAsync()
    {
        if (SelectedElement is null)
        {
            return Task.FromResult(false);
        }

        var source = GetAppliedElement(SelectedElement.Id);
        var nextZ = Math.Min(
            1_000_000,
            _appliedScene.OfType<CameraElementDefinition>().Select(element => element.ZOrder).DefaultIfEmpty(-1).Max() + 1);
        var duplicate = Copy(
            source,
            id: $"camera-element-{Guid.NewGuid():N}",
            x: ClampCoordinate(source.X + 0.025),
            y: ClampCoordinate(source.Y + 0.025),
            zOrder: nextZ);
        return ApplyCandidateAsync([.. _appliedScene, duplicate], duplicate.Id, closePropertiesOnSuccess: false);
    }

    public Task<bool> DeleteSelectedAsync()
    {
        if (SelectedElement is null)
        {
            return Task.FromResult(false);
        }

        var selectedId = SelectedElement.Id;
        var candidate = _appliedScene.Where(element => !string.Equals(element.Id, selectedId, StringComparison.Ordinal)).ToArray();
        return ApplyCandidateAsync(candidate, null, closePropertiesOnSuccess: true);
    }

    public Task<bool> BringForwardAsync() => ReorderSelectedAsync(1);

    public Task<bool> SendBackwardAsync() => ReorderSelectedAsync(-1);

    public Task<bool> SetSelectedZOrderAsync(int zOrder)
    {
        if (SelectedElement is null)
        {
            return Task.FromResult(false);
        }

        var applied = GetAppliedElement(SelectedElement.Id);
        var replacement = Copy(applied, zOrder: zOrder);
        return ApplyCandidateAsync(ReplaceElement(_appliedScene, replacement), applied.Id, closePropertiesOnSuccess: false);
    }

    public Task<bool> NudgeSelectedAsync(double deltaX, double deltaY)
    {
        if (SelectedElement is null || !double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            return Task.FromResult(false);
        }

        var applied = GetAppliedElement(SelectedElement.Id);
        var replacement = Copy(
            applied,
            x: ClampCoordinate(applied.X + deltaX),
            y: ClampCoordinate(applied.Y + deltaY));
        return ApplyCandidateAsync(ReplaceElement(_appliedScene, replacement), applied.Id, closePropertiesOnSuccess: false);
    }

    public void Dispose()
    {
        if (_cameras is INotifyCollectionChanged observableCameras)
        {
            observableCameras.CollectionChanged -= OnCamerasChanged;
        }
        foreach (var camera in _cameras)
        {
            camera.PropertyChanged -= OnCameraPropertyChanged;
        }
        _runtime = null;
        ClearSelection();
        RaiseCommandState();
    }

    private bool BeginInteraction(
        string elementId,
        EditorPoint pointer,
        InteractionKind kind,
        EditorResizeCorner resizeCorner)
    {
        if (_runtime is null || IsApplying || !pointer.IsFinite || !SelectElement(elementId))
        {
            return false;
        }

        ActiveProperties = null;
        OperatorMessage = null;
        _interaction = new Interaction(
            kind,
            pointer,
            GetAppliedElement(elementId),
            SelectedElement!.Geometry,
            resizeCorner);
        RaisePendingChanged();
        return true;
    }

    private async Task<bool> ApplyCandidateAsync(
        IReadOnlyList<ViewSceneElementDefinition> candidate,
        string? selectedId,
        bool closePropertiesOnSuccess)
    {
        var runtime = _runtime;
        if (runtime is null || IsApplying)
        {
            return false;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsApplying = true;
            OperatorMessage = null;
        }).ConfigureAwait(false);
        try
        {
            await runtime.ApplyViewSceneAsync(ViewId, candidate).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                _appliedScene = [.. candidate];
                RebuildElements(selectedId);
                if (closePropertiesOnSuccess)
                {
                    ActiveProperties = null;
                }
                _definitionApplied(new ViewDefinition(ViewId, ViewName, _appliedScene));
            }).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                RebuildElements(selectedId);
                OperatorMessage = OperatorError.ForAction("View scene", "apply", exception);
            }).ConfigureAwait(false);
            return false;
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsApplying = false).ConfigureAwait(false);
        }
    }

    private Task<bool> ReorderSelectedAsync(int direction)
    {
        if (SelectedElement is null)
        {
            return Task.FromResult(false);
        }

        var ordered = _appliedScene
            .OfType<CameraElementDefinition>()
            .OrderBy(element => element.ZOrder)
            .ThenBy(element => element.Id, StringComparer.Ordinal)
            .ToList();
        var index = ordered.FindIndex(element => string.Equals(element.Id, SelectedElement.Id, StringComparison.Ordinal));
        var target = index + direction;
        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return Task.FromResult(false);
        }

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        var normalized = ordered
            .Select((element, zOrder) => (ViewSceneElementDefinition)Copy(element, zOrder: zOrder))
            .ToArray();
        return ApplyCandidateAsync(normalized, SelectedElement.Id, closePropertiesOnSuccess: false);
    }

    private (double X, double Y) SnapPosition(CameraElementDefinition element, double x, double y)
    {
        var camera = FindCamera(element.CameraId);
        var geometry = ViewEditorGeometry.Calculate(
            Copy(element, x: x, y: y),
            camera?.LatestFrameWidth ?? 0,
            camera?.LatestFrameHeight ?? 0);
        var xOffset = FindSnapOffset(AxisCoordinates(geometry.VisibleCorners, vertical: false), false, element.Id);
        var yOffset = FindSnapOffset(AxisCoordinates(geometry.VisibleCorners, vertical: true), true, element.Id);
        return (x + xOffset, y + yOffset);
    }

    private double SnapCoordinate(double coordinate, bool vertical, string excludedElementId)
    {
        var offset = FindSnapOffset([coordinate], vertical, excludedElementId);
        return coordinate + offset;
    }

    private double FindSnapOffset(IReadOnlyList<double> movingCoordinates, bool vertical, string excludedElementId)
    {
        var targets = new List<double> { 0, 0.5, 1 };
        foreach (var element in Elements.Where(candidate => !string.Equals(candidate.Id, excludedElementId, StringComparison.Ordinal)))
        {
            var definition = element.Definition;
            if (!definition.Visible || !definition.Enabled)
            {
                continue;
            }
            targets.AddRange(AxisCoordinates(element.Geometry.VisibleCorners, vertical));
        }

        var best = 0d;
        var bestDistance = SnapTolerance + double.Epsilon;
        foreach (var moving in movingCoordinates)
        {
            foreach (var target in targets)
            {
                var offset = target - moving;
                var distance = Math.Abs(offset);
                if (distance <= SnapTolerance && distance < bestDistance)
                {
                    best = offset;
                    bestDistance = distance;
                }
            }
        }
        return best;
    }

    private static IReadOnlyList<double> AxisCoordinates(
        IReadOnlyList<EditorPoint> corners,
        bool vertical)
    {
        var values = corners.Select(point => vertical ? point.Y : point.X).ToArray();
        var minimum = values.Min();
        var maximum = values.Max();
        return [minimum, (minimum + maximum) / 2, maximum];
    }

    private void RebuildElements(string? selectedId)
    {
        Elements.Clear();
        foreach (var definition in _appliedScene.OfType<CameraElementDefinition>())
        {
            var camera = FindCamera(definition.CameraId);
            Elements.Add(new ViewEditorElementViewModel(definition, camera?.Name ?? definition.CameraId, camera));
        }
        SelectedElement = selectedId is null
            ? null
            : Elements.FirstOrDefault(element => string.Equals(element.Id, selectedId, StringComparison.Ordinal));
        RaisePropertyChanged(nameof(Elements));
    }

    private CameraElementDefinition GetAppliedElement(string elementId)
        => _appliedScene
            .OfType<CameraElementDefinition>()
            .Single(element => string.Equals(element.Id, elementId, StringComparison.Ordinal));

    private static IReadOnlyList<ViewSceneElementDefinition> ReplaceElement(
        IReadOnlyList<ViewSceneElementDefinition> scene,
        CameraElementDefinition replacement)
        => scene
            .Select(element => string.Equals(element.Id, replacement.Id, StringComparison.Ordinal)
                ? replacement
                : element)
            .ToArray();

    private CameraItemViewModel? FindCamera(string cameraId)
        => _cameras.FirstOrDefault(camera => string.Equals(
            camera.Definition.Id,
            cameraId,
            StringComparison.Ordinal));

    private void OnCameraPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not CameraItemViewModel camera
            || eventArgs.PropertyName is not nameof(CameraItemViewModel.LatestFrameWidth)
                and not nameof(CameraItemViewModel.LatestFrameHeight))
        {
            return;
        }

        foreach (var element in Elements.Where(element => string.Equals(
                     element.CameraId,
                     camera.Definition.Id,
                     StringComparison.Ordinal)))
        {
            element.NotifySourceGeometryChanged();
        }
    }

    private void OnCamerasChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.OldItems is not null)
        {
            foreach (var camera in eventArgs.OldItems.OfType<CameraItemViewModel>())
            {
                camera.PropertyChanged -= OnCameraPropertyChanged;
            }
        }
        if (eventArgs.NewItems is not null)
        {
            foreach (var camera in eventArgs.NewItems.OfType<CameraItemViewModel>())
            {
                camera.PropertyChanged += OnCameraPropertyChanged;
            }
        }
    }

    private static EditorPoint OppositeCorner(
        IReadOnlyList<EditorPoint> corners,
        EditorResizeCorner resizeCorner)
        => resizeCorner switch
        {
            EditorResizeCorner.TopLeft => corners[2],
            EditorResizeCorner.TopRight => corners[3],
            EditorResizeCorner.BottomRight => corners[0],
            _ => corners[1],
        };

    private static CameraElementDefinition Copy(
        CameraElementDefinition source,
        string? id = null,
        double? x = null,
        double? y = null,
        double? width = null,
        double? height = null,
        int? zOrder = null,
        double? rotationDegrees = null)
        => new(
            id ?? source.Id,
            source.CameraId,
            x ?? source.X,
            y ?? source.Y,
            width ?? source.Width,
            height ?? source.Height,
            zOrder ?? source.ZOrder,
            source.CropLeft,
            source.CropTop,
            source.CropRight,
            source.CropBottom,
            rotationDegrees ?? source.RotationDegrees,
            source.FlipHorizontal,
            source.FlipVertical,
            source.Visible,
            source.Enabled,
            source.FitMode);

    private static bool DefinitionsEqual(
        CameraElementDefinition left,
        CameraElementDefinition right)
        => string.Equals(left.Id, right.Id, StringComparison.Ordinal)
           && string.Equals(left.CameraId, right.CameraId, StringComparison.Ordinal)
           && left.X == right.X
           && left.Y == right.Y
           && left.Width == right.Width
           && left.Height == right.Height
           && left.ZOrder == right.ZOrder
           && left.CropLeft == right.CropLeft
           && left.CropTop == right.CropTop
           && left.CropRight == right.CropRight
           && left.CropBottom == right.CropBottom
           && left.RotationDegrees == right.RotationDegrees
           && left.FlipHorizontal == right.FlipHorizontal
           && left.FlipVertical == right.FlipVertical
           && left.Visible == right.Visible
           && left.Enabled == right.Enabled
           && left.FitMode == right.FitMode;

    private static double ClampCoordinate(double value)
        => Math.Clamp(
            value,
            -ViewSceneElementDefinition.MaximumNormalizedMagnitude,
            ViewSceneElementDefinition.MaximumNormalizedMagnitude);

    private void RaisePendingChanged() => RaisePropertyChanged(nameof(HasPendingTransform));

    private void RaiseCommandState()
    {
        RaisePropertyChanged(nameof(HasSelection));
    }
}
