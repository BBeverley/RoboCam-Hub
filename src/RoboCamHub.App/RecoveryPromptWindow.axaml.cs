using Avalonia.Controls;
using Avalonia.Interactivity;
using RoboCamHub.Persistence;

namespace RoboCamHub.App;

internal enum RecoveryDecision
{
    Later = 0,
    Recover = 1,
    Discard = 2,
}

public partial class RecoveryPromptWindow : Window
{
    public RecoveryPromptWindow()
    {
        InitializeComponent();
        MessageText.Text = "A newer recovery snapshot is available.";
    }

    public RecoveryPromptWindow(RecoveryEntry recovery)
    {
        InitializeComponent();
        var source = recovery.SourcePath is null ? "an unsaved new show" : Path.GetFileName(recovery.SourcePath);
        MessageText.Text = $"A recovery snapshot for {source} was written at {recovery.RecoveryUtc.LocalDateTime:g}.";
    }

    private void OnLater(object? sender, RoutedEventArgs eventArgs) => Close(RecoveryDecision.Later);

    private void OnRecover(object? sender, RoutedEventArgs eventArgs) => Close(RecoveryDecision.Recover);

    private void OnDiscard(object? sender, RoutedEventArgs eventArgs) => Close(RecoveryDecision.Discard);
}
