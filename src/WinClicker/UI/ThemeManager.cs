using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinClicker.Models;

namespace WinClicker.UI;

internal static class ThemeManager
{
    internal static void Apply(AccentTheme theme, int surfaceOpacityPercent)
    {
        var (accent, hover, pressed, muted) = theme switch
        {
            AccentTheme.Blue => (Color.FromRgb(63, 131, 248), Color.FromRgb(91, 153, 255), Color.FromRgb(43, 100, 205), Color.FromRgb(19, 36, 66)),
            AccentTheme.Purple => (Color.FromRgb(166, 93, 255), Color.FromRgb(188, 126, 255), Color.FromRgb(125, 62, 205), Color.FromRgb(45, 25, 63)),
            AccentTheme.Emerald => (Color.FromRgb(39, 201, 137), Color.FromRgb(69, 222, 160), Color.FromRgb(24, 151, 100), Color.FromRgb(17, 53, 42)),
            AccentTheme.Amber => (Color.FromRgb(255, 164, 48), Color.FromRgb(255, 184, 83), Color.FromRgb(205, 119, 19), Color.FromRgb(62, 41, 16)),
            _ => (Color.FromRgb(255, 59, 53), Color.FromRgb(255, 91, 82), Color.FromRgb(217, 37, 35), Color.FromRgb(58, 23, 25))
        };

        SetColor("AccentColor", accent);
        SetColor("AccentHoverColor", hover);
        SetColor("AccentPressedColor", pressed);
        SetColor("AccentMutedColor", muted);
        SetBrush("AccentBrush", accent);
        SetBrush("AccentHoverBrush", hover);
        SetBrush("AccentPressedBrush", pressed);
        SetBrush("AccentMutedBrush", muted);

        var alpha = (byte)Math.Round(Math.Clamp(surfaceOpacityPercent, 72, 100) / 100d * 255);
        Application.Current.Resources["WindowSurfaceBrush"] =
            new SolidColorBrush(Color.FromArgb(alpha, 11, 13, 16));
    }

    internal static void ApplyWindowBackdrop(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        var corners = NativeMethods.DwmwcpRound;
        var backdrop = NativeMethods.DwmsbtMainWindow;
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DwmwaWindowCornerPreference, ref corners, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DwmwaSystemBackdropType, ref backdrop, sizeof(int));
    }

    private static void SetColor(string key, Color color) => Application.Current.Resources[key] = color;

    private static void SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }
}
