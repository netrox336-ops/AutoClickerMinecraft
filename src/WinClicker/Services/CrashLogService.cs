using System.Reflection;
using System.Text;

namespace WinClicker.Services;

internal static class CrashLogService
{
    internal static void Write(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoClicker",
                "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.0.1";
            var content = new StringBuilder()
                .AppendLine("Auto Clicker crash report")
                .AppendLine($"UTC: {DateTime.UtcNow:O}")
                .AppendLine($"Version: {version}")
                .AppendLine($"OS: {Environment.OSVersion.VersionString}")
                .AppendLine($"Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}")
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();
            File.WriteAllText(path, content, new UTF8Encoding(false));

            foreach (var stale in Directory
                         .EnumerateFiles(directory, "crash-*.log")
                         .OrderByDescending(File.GetCreationTimeUtc)
                         .Skip(8))
            {
                File.Delete(stale);
            }
        }
        catch
        {
            // Crash logging must never cause a second crash.
        }
    }
}
