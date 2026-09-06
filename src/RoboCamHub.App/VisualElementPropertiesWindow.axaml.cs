using Avalonia.Controls;
using Avalonia.Interactivity;
using RoboCamHub.Application;

namespace RoboCamHub.App;

public partial class VisualElementPropertiesWindow : Window
{
    private ViewEditorViewModel? _editor;
    private bool _applied;

    public VisualElementPropertiesWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public VisualElementPropertiesWindow(ViewEditorViewModel editor) : this()
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
            if (await _editor.ApplyVisualPropertiesAsync())
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
