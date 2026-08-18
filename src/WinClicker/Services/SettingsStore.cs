using System.Text;
using System.Text.Json;
using WinClicker.Models;

namespace WinClicker.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly string _legacySettingsPath;

    internal SettingsStore(string? testDirectory = null, bool? forcePortable = null)
    {
        var executableDirectory = testDirectory ?? AppContext.BaseDirectory;
        IsPortable = forcePortable ?? File.Exists(Path.Combine(executableDirectory, "portable.flag"));

        if (testDirectory is not null || IsPortable)
        {
            _settingsPath = Path.Combine(executableDirectory, "data", "settings.json");
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsPath = Path.Combine(localAppData, "AutoClicker", "settings.json");
        }

        _backupPath = _settingsPath + ".bak";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _legacySettingsPath = Path.Combine(local, "WinClicker", "settings.json");
    }

    internal bool IsPortable { get; }
    internal string SettingsPath => _settingsPath;
    internal string? LastRecoveryMessage { get; private set; }

    internal AppSettings Load()
    {
        LastRecoveryMessage = null;
        var sourcePath = ResolveSourcePath();
        if (sourcePath is null)
        {
            return NewDefaults();
        }

        if (TryRead(sourcePath, out var settings))
        {
            if (!string.Equals(sourcePath, _settingsPath, StringComparison.OrdinalIgnoreCase))
            {
                Save(settings);
            }

            return settings;
        }

        QuarantineCorruptedFile(sourcePath);
        if (TryRead(_backupPath, out settings))
        {
            LastRecoveryMessage = "Повреждённые настройки заменены последней резервной копией.";
            Save(settings);
            return settings;
        }

        LastRecoveryMessage = "Повреждённые настройки изолированы. Загружена безопасная конфигурация.";
        return NewDefaults();
    }

    internal bool Save(AppSettings settings)
    {
        try
        {
            settings.Normalize();
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _settingsPath + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(temporaryPath, _settingsPath, _backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
                File.Copy(_settingsPath, _backupPath, true);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal AppSettings Import(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var settings = DeserializeAndMigrate(json);
        settings.Normalize();
        return settings;
    }

    internal void Export(string path, AppSettings settings)
    {
        settings.Normalize();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    private string? ResolveSourcePath()
    {
        if (File.Exists(_settingsPath))
        {
            return _settingsPath;
        }

        if (File.Exists(_legacySettingsPath))
        {
            return _legacySettingsPath;
        }

        return null;
    }

    private static AppSettings NewDefaults()
    {
        var settings = new AppSettings();
        settings.Normalize();
        return settings;
    }

    private static bool TryRead(string path, out AppSettings settings)
    {
        settings = NewDefaults();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            settings = DeserializeAndMigrate(json);
            settings.Normalize();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static AppSettings DeserializeAndMigrate(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        var root = document.RootElement;

        if (TryGetInt(root, "IntervalMs", out var legacyInterval))
        {
            if (!root.TryGetProperty("LeftIntervalMs", out _))
            {
                settings.LeftIntervalMs = legacyInterval;
            }

            if (!root.TryGetProperty("RightIntervalMs", out _))
            {
                settings.RightIntervalMs = legacyInterval;
            }
        }

        MigrateV2Bindings(root, settings);
        return settings;
    }

    private static void MigrateV2Bindings(JsonElement root, AppSettings settings)
    {
        if (root.TryGetProperty("ToggleHotkey", out var leftElement))
        {
            settings.LeftHotkey = leftElement.Deserialize<HotkeyBinding>(JsonOptions) ?? settings.LeftHotkey;
        }

        if (root.TryGetProperty("SwitchButtonHotkey", out var rightElement))
        {
            settings.RightHotkey = rightElement.Deserialize<HotkeyBinding>(JsonOptions) ?? settings.RightHotkey;
        }

        if (TryGetInt(root, "ToggleHotkeyMode", out var leftMode)
            && Enum.IsDefined((HotkeyTriggerMode)leftMode))
        {
            settings.LeftHotkeyMode = (HotkeyTriggerMode)leftMode;
        }

        if (TryGetInt(root, "SwitchButtonHotkeyMode", out var rightMode)
            && Enum.IsDefined((HotkeyTriggerMode)rightMode))
        {
            settings.RightHotkeyMode = (HotkeyTriggerMode)rightMode;
        }
    }

    private void QuarantineCorruptedFile(string path)
    {
        if (!string.Equals(path, _settingsPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var quarantinePath = _settingsPath + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(path, quarantinePath, true);
        }
        catch
        {
            // Loading safe defaults is more important than preserving an unreadable file.
        }
    }

    private static bool TryGetInt(JsonElement root, string propertyName, out int value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var element) && element.TryGetInt32(out value);
    }
}
