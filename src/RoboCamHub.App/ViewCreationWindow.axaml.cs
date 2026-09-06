using Avalonia.Controls;
using Avalonia.Interactivity;
using RoboCamHub.Application;
using RoboCamHub.Domain;

namespace RoboCamHub.App;

public partial class ViewCreationWindow : Window
{
    private readonly ViewTemplateFactory _factory = new();

    public ViewCreationWindow()
    {
        InitializeComponent();
    }

    public ViewCreationWindow(ViewCreationViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        if (viewModel.IsDuplicate)
        {
            Height = 300;
            MinHeight = 300;
            CanResize = false;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs eventArgs) => Close(null);

    private void OnSubmit(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is ViewCreationViewModel viewModel
            && viewModel.TryBuildDefinition(_factory, out var definition))
        {
            Close(definition);
        }
    }

    private void OnClearAssignment(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { DataContext: ViewTemplateSlotAssignmentViewModel assignment })
        {
            assignment.SelectedCamera = null;
        }
    }
}
