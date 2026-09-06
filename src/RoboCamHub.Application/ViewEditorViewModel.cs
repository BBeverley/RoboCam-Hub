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
        ViewSceneElementDefinition StartDefinition,
        EditorElementGeometry StartGeometry,
        EditorResizeCorner ResizeCorner);

    private IWorkspaceRuntimeService? _runtime;
    private readonly IUiDispatcher _dispatcher;
    private readonly IReadOnlyList<CameraItemViewModel> _cameras;
    private readonly WorkspaceCapabilities _capabilities;
    private IReadOnlyList<AssetDefinition> _assets;
    private readonly Action<ViewDefinition> _definitionApplied;
    private IReadOnlyList<ViewSceneElementDefinition> _appliedScene;
    private ViewEditorElementViewModel? _selectedElement;
    private CameraElementPropertiesViewModel? _activeProperties;
    private VisualElementPropertiesViewModel? _activeVisualProperties;
    private Interaction _interaction;
    private bool _isApplying;
    private string? _operatorMessage;

    internal ViewEditorViewModel(
        ViewDefinition definition,
        IReadOnlyList<CameraItemViewModel> cameras,
        IWorkspaceRuntimeService runtime,
        IUiDispatcher dispatcher,
        WorkspaceCapabilities capabilities,
        Action<ViewDefinition> definitionApplied)
    {
        ViewId = definition.Id;
        ViewName = definition.Name;
        _cameras = cameras;
        _runtime = runtime;
        _dispatcher = dispatcher;
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _capabilities.PropertyChanged += OnCapabilitiesChanged;
        _definitionApplied = definitionApplied;
        _appliedScene = [.. definition.SceneElements];
        _assets = [.. definition.Assets];
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

    public bool CanEditScene => _capabilities.CanEditScene;

    public bool HasPendingTransform => _interaction.Kind != InteractionKind.None;

    public bool HasPendingProperties => ActiveProperties is not null || ActiveVisualProperties is not null;

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

    public VisualElementPropertiesViewModel? ActiveVisualProperties
    {
        get => _activeVisualProperties;
        private set
        {
            if (SetProperty(ref _activeVisualProperties, value))
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

    public void ReportOperatorError(string action, Exception exception)
        => OperatorMessage = OperatorError.ForAction("View scene", action, exception);

    public ViewEditorElementViewModel? HitTest(EditorPoint point)
    {
        if (!point.IsFinite)
        {
            return null;
        }

        return Elements
            .Where(element => element.IsVisibleOnCanvas && element.HitTest(point))
            .OrderByDescending(element => element.ZOrder)
            .ThenByDescending(element => element.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public ViewEditorElementViewModel? SelectAt(EditorPoint point)
    {
        if (!CanEditScene)
        {
            return null;
        }
        SelectedElement = HitTest(point);
        return SelectedElement;
    }

    public bool SelectElement(string elementId)
    {
        if (!CanEditScene)
        {
            return false;
        }
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

        if (preserveAspectRatio || IsContained(start))
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
        var (sourceWidth, sourceHeight) = GetSourceDimensions(start);
        var sizedGeometry = ViewEditorGeometry.Calculate(
            sized,
            sourceWidth,
            sourceHeight);
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
        if (!CanEditScene)
        {
            CancelInteraction();
            return Task.FromResult(false);
        }
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
        if (!CanEditScene || SelectedElement?.Definition is not CameraElementDefinition camera)
        {
            return null;
        }

        ActiveProperties = new CameraElementPropertiesViewModel(camera);
        ActiveVisualProperties = null;
        OperatorMessage = null;
        return ActiveProperties;
    }

    public VisualElementPropertiesViewModel? BeginVisualProperties()
    {
        if (!CanEditScene || SelectedElement?.Definition is null or CameraElementDefinition)
        {
            return null;
        }
        ActiveVisualProperties = new VisualElementPropertiesViewModel(GetAppliedElement(SelectedElement.Id));
        ActiveProperties = null;
        OperatorMessage = null;
        return ActiveVisualProperties;
    }

    public void CancelProperties()
    {
        ActiveProperties = null;
        ActiveVisualProperties = null;
    }

    public async Task<bool> ApplyVisualPropertiesAsync()
    {
        var properties = ActiveVisualProperties;
        if (properties is null)
        {
            return false;
        }
        try
        {
            var replacement = properties.ToDefinition();
            return await ApplyCandidateAsync(
                    ReplaceElement(_appliedScene, replacement),
                    properties.ElementId,
                    closePropertiesOnSuccess: true)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(() => OperatorMessage = OperatorError.ForAction("Element properties", "apply", exception))
                .ConfigureAwait(false);
            return false;
        }
    }

    public async Task<bool> ApplyPropertiesAsync()
    {
        var properties = ActiveProperties;
        if (properties is null)
        {
            return false;
        }

        try
        {
            var applied = (CameraElementDefinition)GetAppliedElement(properties.ElementId);
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

        var nextZ = _appliedScene.Select(element => element.ZOrder).DefaultIfEmpty(-1).Max();
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

    public Task<bool> AddTextAsync()
    {
        var element = new TextElementDefinition(
            $"text-element-{Guid.NewGuid():N}",
            "Title",
            0.25,
            0.08,
            0.5,
            0.14,
            NextZOrder(),
            fontSize: 64,
            alignment: TextElementAlignment.Center);
        return ApplyCandidateAsync([.. _appliedScene, element], element.Id, false);
    }

    public Task<bool> AddRectangleAsync()
    {
        var element = new ShapeElementDefinition(
            $"rectangle-element-{Guid.NewGuid():N}",
            0.25,
            0.25,
            0.5,
            0.5,
            NextZOrder(),
            0x285078CC,
            0xFFFFFFFF,
            4);
        return ApplyCandidateAsync([.. _appliedScene, element], element.Id, false);
    }

    public Task<bool> AddFrameAsync()
    {
        var element = new FrameElementDefinition(
            $"frame-element-{Guid.NewGuid():N}",
            0.2,
            0.2,
            0.6,
            0.6,
            NextZOrder(),
            0xFFFFFFFF,
            8);
        return ApplyCandidateAsync([.. _appliedScene, element], element.Id, false);
    }

    public Task<bool> AddImageAsync(AssetDefinition asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var assets = _assets.Any(existing => string.Equals(existing.Id, asset.Id, StringComparison.Ordinal))
            ? _assets
            : [.. _assets, asset];
        var element = new ImageElementDefinition(
            $"image-element-{Guid.NewGuid():N}",
            asset.Id,
            0.3,
            0.3,
            0.4,
            0.4,
            NextZOrder());
        return ApplyCandidateAsync([.. _appliedScene, element], element.Id, false, assets);
    }

    public Task<bool> DuplicateSelectedAsync()
    {
        if (SelectedElement is null)
        {
            return Task.FromResult(false);
        }

        var source = GetAppliedElement(SelectedElement.Id);
        var nextZ = NextZOrder();
        var duplicate = Copy(
            source,
            id: $"{ElementIdPrefix(source)}-{Guid.NewGuid():N}",
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
        var referencedAssetIds = candidate.OfType<ImageElementDefinition>()
            .Select(element => element.AssetId)
            .ToHashSet(StringComparer.Ordinal);
        var candidateAssets = _assets.Where(asset => referencedAssetIds.Contains(asset.Id)).ToArray();
        return ApplyCandidateAsync(candidate, null, closePropertiesOnSuccess: true, candidateAssets);
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
        _capabilities.PropertyChanged -= OnCapabilitiesChanged;
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
        if (!CanEditScene || _runtime is null || IsApplying || !pointer.IsFinite || !SelectElement(elementId))
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
        bool closePropertiesOnSuccess,
        IReadOnlyList<AssetDefinition>? candidateAssets = null)
    {
        var runtime = _runtime;
        if (!CanEditScene || runtime is null || IsApplying)
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
            var assets = candidateAssets ?? _assets;
            await runtime.ApplyViewSceneAsync(ViewId, candidate, assets).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                _appliedScene = [.. candidate];
                _assets = [.. assets];
                RebuildElements(selectedId);
                if (closePropertiesOnSuccess)
                {
                    ActiveProperties = null;
                    ActiveVisualProperties = null;
                }
                _definitionApplied(new ViewDefinition(ViewId, ViewName, _appliedScene, _assets));
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
            .Select((element, zOrder) => Copy(element, zOrder: zOrder))
            .ToArray();
        return ApplyCandidateAsync(normalized, SelectedElement.Id, closePropertiesOnSuccess: false);
    }

    private (double X, double Y) SnapPosition(ViewSceneElementDefinition element, double x, double y)
    {
        var (sourceWidth, sourceHeight) = GetSourceDimensions(element);
        var geometry = ViewEditorGeometry.Calculate(
            Copy(element, x: x, y: y),
            sourceWidth,
            sourceHeight);
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
        foreach (var definition in _appliedScene)
        {
            var camera = definition is CameraElementDefinition cameraElement
                ? FindCamera(cameraElement.CameraId)
                : null;
            var asset = definition is ImageElementDefinition imageElement
                ? _assets.FirstOrDefault(candidate => string.Equals(candidate.Id, imageElement.AssetId, StringComparison.Ordinal))
                : null;
            var name = definition switch
            {
                CameraElementDefinition item => camera?.Name ?? item.CameraId,
                TextElementDefinition item => item.Text,
                ImageElementDefinition item => asset?.DisplayName ?? item.AssetId,
                ShapeElementDefinition => "Rectangle",
                FrameElementDefinition => "Frame",
                _ => definition.Id,
            };
            Elements.Add(new ViewEditorElementViewModel(definition, name, camera, asset));
        }
        SelectedElement = selectedId is null
            ? null
            : Elements.FirstOrDefault(element => string.Equals(element.Id, selectedId, StringComparison.Ordinal));
        RaisePropertyChanged(nameof(Elements));
    }

    private ViewSceneElementDefinition GetAppliedElement(string elementId)
        => _appliedScene
            .Single(element => string.Equals(element.Id, elementId, StringComparison.Ordinal));

    private static IReadOnlyList<ViewSceneElementDefinition> ReplaceElement(
        IReadOnlyList<ViewSceneElementDefinition> scene,
        ViewSceneElementDefinition replacement)
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

    private static ViewSceneElementDefinition Copy(
        ViewSceneElementDefinition source,
        string? id = null,
        double? x = null,
        double? y = null,
        double? width = null,
        double? height = null,
        int? zOrder = null,
        double? rotationDegrees = null)
        => source switch
        {
            CameraElementDefinition camera => new CameraElementDefinition(
                id ?? camera.Id, camera.CameraId, x ?? camera.X, y ?? camera.Y,
                width ?? camera.Width, height ?? camera.Height, zOrder ?? camera.ZOrder,
                camera.CropLeft, camera.CropTop, camera.CropRight, camera.CropBottom,
                rotationDegrees ?? camera.RotationDegrees, camera.FlipHorizontal,
                camera.FlipVertical, camera.Visible, camera.Enabled, camera.FitMode),
            TextElementDefinition text => new TextElementDefinition(
                id ?? text.Id, text.Text, x ?? text.X, y ?? text.Y, width ?? text.Width,
                height ?? text.Height, zOrder ?? text.ZOrder, text.FontFamily, text.FontSize,
                text.Alignment, text.Weight, text.Style, text.TextColorRgba, text.BackgroundColorRgba,
                rotationDegrees ?? text.RotationDegrees, text.FlipHorizontal, text.FlipVertical,
                text.Visible, text.Enabled, text.VerticalAlignment, text.Underline),
            ImageElementDefinition image => new ImageElementDefinition(
                id ?? image.Id, image.AssetId, x ?? image.X, y ?? image.Y, width ?? image.Width,
                height ?? image.Height, zOrder ?? image.ZOrder, image.FitMode, image.Opacity,
                rotationDegrees ?? image.RotationDegrees, image.FlipHorizontal, image.FlipVertical,
                image.Visible, image.Enabled),
            ShapeElementDefinition rectangle => new ShapeElementDefinition(
                id ?? rectangle.Id, x ?? rectangle.X, y ?? rectangle.Y, width ?? rectangle.Width,
                height ?? rectangle.Height, zOrder ?? rectangle.ZOrder, rectangle.FillColorRgba,
                rectangle.OutlineColorRgba, rectangle.OutlineWidth, rectangle.Opacity,
                rotationDegrees ?? rectangle.RotationDegrees, rectangle.Visible, rectangle.Enabled),
            FrameElementDefinition frame => new FrameElementDefinition(
                id ?? frame.Id, x ?? frame.X, y ?? frame.Y, width ?? frame.Width,
                height ?? frame.Height, zOrder ?? frame.ZOrder, frame.ColorRgba,
                frame.Thickness, frame.Opacity, rotationDegrees ?? frame.RotationDegrees,
                frame.Visible, frame.Enabled),
            _ => throw new NotSupportedException($"Scene element type '{source.GetType().Name}' is unsupported."),
        };

    private static bool DefinitionsEqual(
        ViewSceneElementDefinition left,
        ViewSceneElementDefinition right)
        => string.Equals(left.Id, right.Id, StringComparison.Ordinal)
           && left.X == right.X
           && left.Y == right.Y
           && left.Width == right.Width
           && left.Height == right.Height
           && left.ZOrder == right.ZOrder
           && left.RotationDegrees == right.RotationDegrees
           && left.FlipHorizontal == right.FlipHorizontal
           && left.FlipVertical == right.FlipVertical
           && left.Visible == right.Visible
           && left.Enabled == right.Enabled
           && left.GetType() == right.GetType();

    private int NextZOrder()
        => Math.Min(1_000_000, _appliedScene.Select(element => element.ZOrder).DefaultIfEmpty(-1).Max() + 1);

    private static string ElementIdPrefix(ViewSceneElementDefinition element)
        => element switch
        {
            CameraElementDefinition => "camera-element",
            TextElementDefinition => "text-element",
            ImageElementDefinition => "image-element",
            ShapeElementDefinition => "shape-element",
            FrameElementDefinition => "frame-element",
            _ => "scene-element",
        };

    private static bool IsContained(ViewSceneElementDefinition element)
        => element switch
        {
            CameraElementDefinition camera => camera.FitMode == CameraElementFitMode.Contain,
            ImageElementDefinition image => image.FitMode == CameraElementFitMode.Contain,
            _ => false,
        };

    private (uint Width, uint Height) GetSourceDimensions(ViewSceneElementDefinition element)
        => element switch
        {
            CameraElementDefinition camera => (
                FindCamera(camera.CameraId)?.LatestFrameWidth ?? 0,
                FindCamera(camera.CameraId)?.LatestFrameHeight ?? 0),
            ImageElementDefinition image => (
                _assets.FirstOrDefault(asset => string.Equals(asset.Id, image.AssetId, StringComparison.Ordinal))?.PixelWidth ?? 0,
                _assets.FirstOrDefault(asset => string.Equals(asset.Id, image.AssetId, StringComparison.Ordinal))?.PixelHeight ?? 0),
            _ => (0, 0),
        };

    private static double ClampCoordinate(double value)
        => Math.Clamp(
            value,
            -ViewSceneElementDefinition.MaximumNormalizedMagnitude,
            ViewSceneElementDefinition.MaximumNormalizedMagnitude);

    private void RaisePendingChanged() => RaisePropertyChanged(nameof(HasPendingTransform));

    private void RaiseCommandState()
    {
        RaisePropertyChanged(nameof(HasSelection));
        RaisePropertyChanged(nameof(CanEditScene));
    }

    private void OnCapabilitiesChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not nameof(WorkspaceCapabilities.Mode)
            and not nameof(WorkspaceCapabilities.CanEditScene))
        {
            return;
        }

        if (!CanEditScene)
        {
            ClearSelection();
        }
        RaiseCommandState();
    }
}
