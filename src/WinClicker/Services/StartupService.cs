using Microsoft.Win32;

namespace WinClicker.Services;

internal static class StartupService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoClicker";

    internal static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    internal static bool SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            if (enabled)
            {
                var executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AutoClicker.exe");
                key.SetValue(ValueName, $"\"{executable}\" --minimized", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
