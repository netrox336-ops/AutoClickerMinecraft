using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WinClicker.Models;

namespace WinClicker.Services;

internal static class DiagnosticService
{
    internal static void Save(
        string path,
        AppSettings settings,
        bool hookHealthy,
        bool portable,
        long leftClicks,
        long rightClicks)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var report = new
        {
            Product = "Auto Clicker",
            Version = assembly.GetName().Version?.ToString(3) ?? "3.0.1",
            GeneratedUtc = DateTime.UtcNow,
            Windows = Environment.OSVersion.VersionString,
            Framework = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            WorkingSetMb = Math.Round(Environment.WorkingSet / 1024d / 1024d, 1),
            HookHealthy = hookHealthy,
            StorageMode = portable ? "Portable" : "Installed",
            Session = new
            {
                LeftDeliveredClicks = leftClicks,
                RightDeliveredClicks = rightClicks
            },
            Configuration = new
            {
                settings.LeftIntervalMs,
                settings.RightIntervalMs,
                LeftHotkey = settings.LeftHotkey.ToDisplayString(),
                RightHotkey = settings.RightHotkey.ToDisplayString(),
                PanicHotkey = settings.PanicHotkey.ToDisplayString(),
                settings.LeftHotkeyMode,
                settings.RightHotkeyMode,
                settings.OverlayEnabled,
                settings.OverlayDisplayMode,
                settings.OverlayScalePercent,
                settings.AccentTheme,
                settings.ReduceMotion
            },
            Privacy = "Отчёт не содержит имени пользователя, путей профиля, координат курсора или списка запущенных приложений."
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }
}
