using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RoboCamHub.App;

internal enum SaveChangesDecision
{
    Cancel = 0,
    Save = 1,
    DontSave = 2,
}

public partial class SaveChangesWindow : Window
{
    public SaveChangesWindow()
        : this("This show")
    {
    }

    public SaveChangesWindow(string displayName)
    {
        InitializeComponent();
        MessageText.Text = $"{displayName} has durable changes that have not been saved to its main .rchshow file.";
    }

    private void OnCancel(object? sender, RoutedEventArgs eventArgs) => Close(SaveChangesDecision.Cancel);

    private void OnSave(object? sender, RoutedEventArgs eventArgs) => Close(SaveChangesDecision.Save);

    private void OnDontSave(object? sender, RoutedEventArgs eventArgs) => Close(SaveChangesDecision.DontSave);
}
