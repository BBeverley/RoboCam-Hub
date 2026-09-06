using Avalonia.Controls;
using Avalonia.Interactivity;
using RoboCamHub.Application;

namespace RoboCamHub.App;

public partial class CameraElementPickerWindow : Window
{
    public CameraElementPickerWindow()
    {
        InitializeComponent();
    }

    public CameraElementPickerWindow(IReadOnlyList<CameraItemViewModel> cameras) : this()
    {
        CameraPicker.ItemsSource = cameras;
        CameraPicker.SelectedIndex = cameras.Count == 0 ? -1 : 0;
    }

    private void OnAdd(object? sender, RoutedEventArgs eventArgs)
        => Close(CameraPicker.SelectedItem as CameraItemViewModel);

    private void OnCancel(object? sender, RoutedEventArgs eventArgs) => Close(null);
}
