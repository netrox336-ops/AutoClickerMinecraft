using System.Windows;

namespace WinClicker.UI;

public partial class ConfirmDialog : Window
{
    internal ConfirmDialog(string title, string message, string yesText, string noText)
    {
        InitializeComponent();
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        YesButton.Content = yesText;
        NoButton.Content = noText;
    }

    internal static bool Show(
        Window owner,
        string title,
        string message,
        string yesText = "ПРОДОЛЖИТЬ",
        string noText = "ОТМЕНА")
    {
        var dialog = new ConfirmDialog(title, message, yesText, noText) { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    internal static void ShowInfo(Window owner, string title, string message)
    {
        var dialog = new ConfirmDialog(title, message, "ЗАКРЫТЬ", string.Empty) { Owner = owner };
        dialog.NoButton.Visibility = Visibility.Collapsed;
        dialog.ShowDialog();
    }

    private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void No_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
