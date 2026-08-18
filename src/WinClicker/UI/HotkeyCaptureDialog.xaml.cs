using System.Windows;
using WinClicker.Models;

namespace WinClicker.UI;

public partial class HotkeyCaptureDialog : Window
{
    internal HotkeyCaptureDialog(string actionName, HotkeyBinding current)
    {
        InitializeComponent();
        TitleText.Text = actionName.ToUpperInvariant();
        CurrentBindingText.Text = $"Сейчас: {current.ToDisplayString()}";
    }

    internal HotkeyBinding? CapturedBinding { get; private set; }

    internal void Complete(HotkeyBinding binding)
    {
        CapturedBinding = binding;
        CurrentBindingText.Text = binding.ToDisplayString();
        DialogResult = true;
    }

    internal void CancelCapture()
    {
        CapturedBinding = null;
        DialogResult = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelCapture();

    private void Dialog_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyWindowBackdrop(this);
}
