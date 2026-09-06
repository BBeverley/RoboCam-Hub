using Avalonia.Controls;
using Avalonia.Interactivity;
using RoboCamHub.Application;

namespace RoboCamHub.App;

public partial class CameraElementPropertiesWindow : Window
{
    private ViewEditorViewModel? _editor;
    private bool _applied;

    public CameraElementPropertiesWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public CameraElementPropertiesWindow(ViewEditorViewModel editor)
        : this()
    {
        _editor = editor;
        DataContext = editor;
    }

    private async void OnApply(object? sender, RoutedEventArgs eventArgs)
    {
        if (_editor is null)
        {
            return;
        }

        ApplyButton.IsEnabled = false;
        try
        {
            if (await _editor.ApplyPropertiesAsync())
            {
                _applied = true;
                Close();
            }
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs eventArgs) => Close();

    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (!_applied)
        {
            _editor?.CancelProperties();
        }
    }
}
