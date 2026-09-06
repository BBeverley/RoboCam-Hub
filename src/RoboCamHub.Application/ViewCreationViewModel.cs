using System.Collections.ObjectModel;
using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public sealed record ViewTemplateChoiceViewModel(
    string DisplayName,
    ViewTemplateDefinition? Template);

public sealed class ViewTemplateSlotAssignmentViewModel : ObservableObject
{
    private CameraItemViewModel? _selectedCamera;

    internal ViewTemplateSlotAssignmentViewModel(
        ViewTemplateSlotDefinition slot,
        IReadOnlyList<CameraItemViewModel> availableCameras)
    {
        Slot = slot;
        AvailableCameras = availableCameras;
    }

    public ViewTemplateSlotDefinition Slot { get; }

    public string DisplayLabel => Slot.DisplayLabel ?? Slot.Id;

    public IReadOnlyList<CameraItemViewModel> AvailableCameras { get; }

    public CameraItemViewModel? SelectedCamera
    {
        get => _selectedCamera;
        set => SetProperty(ref _selectedCamera, value);
    }
}

public sealed class ViewCreationViewModel : ObservableObject
{
    private readonly IReadOnlyList<CameraItemViewModel> _cameras;
    private readonly ViewDefinition? _duplicateSource;
    private string _viewName;
    private ViewTemplateChoiceViewModel? _selectedTemplateChoice;
    private string? _operatorMessage;

    private ViewCreationViewModel(
        IReadOnlyList<CameraItemViewModel> cameras,
        ViewDefinition? duplicateSource)
    {
        _cameras = cameras;
        _duplicateSource = duplicateSource;
        IsDuplicate = duplicateSource is not null;
        Title = IsDuplicate ? "Duplicate View" : "Create View";
        SubmitLabel = IsDuplicate ? "Duplicate" : "Create";
        _viewName = IsDuplicate ? $"{duplicateSource!.Name} Copy" : "New View";
        TemplateChoices = IsDuplicate
            ? []
            : [
                new ViewTemplateChoiceViewModel("Blank", null),
                .. BuiltInViewTemplates.All.Select(template => new ViewTemplateChoiceViewModel(template.Name, template)),
            ];
        SlotAssignments = [];
        if (!IsDuplicate)
        {
            SelectedTemplateChoice = TemplateChoices[0];
        }
    }

    public static ViewCreationViewModel Create(
        IReadOnlyList<CameraItemViewModel> cameras)
        => new(cameras ?? throw new ArgumentNullException(nameof(cameras)), null);

    public static ViewCreationViewModel Duplicate(
        IReadOnlyList<CameraItemViewModel> cameras,
        ViewDefinition source)
        => new(
            cameras ?? throw new ArgumentNullException(nameof(cameras)),
            source ?? throw new ArgumentNullException(nameof(source)));

    public string Title { get; }

    public string SubmitLabel { get; }

    public bool IsDuplicate { get; }

    public bool IsTemplateCreation => !IsDuplicate;

    public string DuplicateDescription
        => _duplicateSource is null
            ? string.Empty
            : $"Copy all camera references and transforms from '{_duplicateSource.Name}'. Outputs remain routed to the original View.";

    public IReadOnlyList<ViewTemplateChoiceViewModel> TemplateChoices { get; }

    public ObservableCollection<ViewTemplateSlotAssignmentViewModel> SlotAssignments { get; }

    public bool HasSlotAssignments => SlotAssignments.Count > 0;

    public string ViewName
    {
        get => _viewName;
        set
        {
            if (SetProperty(ref _viewName, value))
            {
                RaisePropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public ViewTemplateChoiceViewModel? SelectedTemplateChoice
    {
        get => _selectedTemplateChoice;
        set
        {
            if (!SetProperty(ref _selectedTemplateChoice, value))
            {
                return;
            }

            SlotAssignments.Clear();
            if (value?.Template is { } template)
            {
                foreach (var slot in template.Slots)
                {
                    SlotAssignments.Add(new ViewTemplateSlotAssignmentViewModel(slot, _cameras));
                }
            }
            RaisePropertyChanged(nameof(HasSlotAssignments));
        }
    }

    public bool CanSubmit => !string.IsNullOrWhiteSpace(ViewName);

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

    public bool TryBuildDefinition(ViewTemplateFactory factory, out ViewDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(factory);
        definition = null;
        OperatorMessage = null;
        try
        {
            if (!CanSubmit)
            {
                throw new InvalidOperationException("View name is required.");
            }

            if (_duplicateSource is not null)
            {
                definition = factory.Duplicate(_duplicateSource, ViewName);
                return true;
            }

            var template = SelectedTemplateChoice?.Template;
            if (template is null)
            {
                definition = factory.CreateBlank(ViewName);
                return true;
            }

            var assignments = SlotAssignments.ToDictionary(
                assignment => assignment.Slot.Id,
                assignment => assignment.SelectedCamera?.Definition.Id,
                StringComparer.Ordinal);
            definition = factory.Instantiate(template, ViewName, assignments);
            return true;
        }
        catch (Exception exception)
        {
            OperatorMessage = OperatorError.ForAction("View", IsDuplicate ? "duplication" : "creation", exception);
            return false;
        }
    }
}
