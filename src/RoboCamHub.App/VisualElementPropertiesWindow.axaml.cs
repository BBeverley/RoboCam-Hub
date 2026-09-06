using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
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
        InitializeVisualControls(editor.ActiveVisualProperties);
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
            if (_editor.ActiveVisualProperties is { } properties)
            {
                properties.PrimaryColor = ToRgba(PrimaryColorPicker.Color);
                properties.SecondaryColor = ToRgba(SecondaryColorPicker.Color);
            }
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

    private void InitializeVisualControls(VisualElementPropertiesViewModel? properties)
    {
        if (properties is null)
        {
            return;
        }

        PrimaryColorPicker.Color = FromRgba(properties.PrimaryColor);
        SecondaryColorPicker.Color = FromRgba(
            string.IsNullOrWhiteSpace(properties.SecondaryColor) ? "#00000000" : properties.SecondaryColor);

        var currentFontFamily = string.IsNullOrWhiteSpace(properties.FontFamily)
            ? FontManager.Current.DefaultFontFamily.Name
            : properties.FontFamily;
        var fontFamilies = FontManager.Current.SystemFonts
            .Select(font => font.Name)
            .Append(currentFontFamily)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        FontFamilyPicker.ItemsSource = fontFamilies;
        FontFamilyPicker.SelectedItem = fontFamilies.FirstOrDefault(
            name => string.Equals(name, currentFontFamily, StringComparison.CurrentCultureIgnoreCase))
            ?? FontManager.Current.DefaultFontFamily.Name;
        properties.FontFamily = currentFontFamily;
    }

    private static Color FromRgba(string value)
    {
        var hex = value.StartsWith('#') ? value[1..] : value;
        if (hex.Length != 8
            || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
        {
            return Colors.Transparent;
        }
        return Color.FromArgb(
            (byte)(rgba & 0xFFU),
            (byte)((rgba >> 24) & 0xFFU),
            (byte)((rgba >> 16) & 0xFFU),
            (byte)((rgba >> 8) & 0xFFU));
    }

    private static string ToRgba(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
}
