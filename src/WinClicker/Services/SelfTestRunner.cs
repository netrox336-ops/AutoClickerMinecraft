using System.Text;
using WinClicker.Models;

namespace WinClicker.Services;

internal static class SelfTestRunner
{
    internal static int Run()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "AutoClickerSelfTest-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            TestHotkeyMatching();
            TestSettingsNormalization();
            TestSettingsRecovery(temporaryDirectory);
            TestLegacyIntervalMigration(temporaryDirectory);
            TestIndependentEngine();
            return 0;
        }
        catch (Exception exception)
        {
            CrashLogService.Write(exception);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, true);
                }
            }
            catch
            {
                // Temporary self-test data can be cleaned by Windows later.
            }
        }
    }

    private static void TestHotkeyMatching()
    {
        var plainF6 = HotkeyBinding.FromKey(VirtualKeys.F1 + 5);
        Assert(plainF6.MatchesKeyboard(VirtualKeys.F1 + 5, false, false, true, false, true),
            "Совместимый режим должен принимать лишний Shift.");
        Assert(!plainF6.MatchesKeyboard(VirtualKeys.F1 + 5, false, false, true, false, false),
            "Строгий режим должен отклонять лишний Shift.");

        var mouse4 = HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse4);
        Assert(mouse4.MatchesMouse(HotkeyMouseButton.Mouse4, false, false, false, false, true),
            "Mouse 4 должен совпадать со своим физическим событием.");
        Assert(!mouse4.MatchesMouse(HotkeyMouseButton.Mouse5, false, false, false, false, true),
            "Mouse 4 и Mouse 5 обязаны оставаться независимыми.");
    }

    private static void TestSettingsNormalization()
    {
        var settings = new AppSettings
        {
            LeftIntervalMs = -20,
            RightIntervalMs = 5000,
            OverlayOpacityPercent = 5,
            OverlayScalePercent = 999,
            WindowSurfaceOpacityPercent = 1,
            LeftHotkey = HotkeyBinding.FromKey(VirtualKeys.F12),
            RightHotkey = HotkeyBinding.FromKey(VirtualKeys.F12),
            PanicHotkey = HotkeyBinding.FromKey(VirtualKeys.F12)
        };
        settings.Normalize();
        Assert(settings.SchemaVersion == AppSettings.CurrentSchemaVersion, "Схема настроек должна обновляться.");
        Assert(settings.LeftIntervalMs == 1, "Минимальный интервал LMB должен быть 1 мс.");
        Assert(settings.RightIntervalMs == 1000, "Максимальный интервал RMB должен быть 1000 мс.");
        Assert(settings.OverlayOpacityPercent == 35, "Overlay не должен становиться невидимым.");
        Assert(settings.OverlayScalePercent == 160, "Масштаб overlay должен нормализоваться.");
        Assert(settings.WindowSurfaceOpacityPercent == 72, "Mica-поверхность должна сохранять читаемость.");
        Assert(!settings.LeftHotkey.Equals(settings.RightHotkey), "Каналы не должны сохранять конфликт.");
        Assert(!settings.LeftHotkey.Equals(settings.PanicHotkey), "LMB не должен конфликтовать с Panic.");
        Assert(!settings.RightHotkey.Equals(settings.PanicHotkey), "RMB не должен конфликтовать с Panic.");
    }

    private static void TestSettingsRecovery(string directory)
    {
        var store = new SettingsStore(directory, true);
        var settings = new AppSettings { LeftIntervalMs = 17, RightIntervalMs = 31 };
        Assert(store.Save(settings), "Настройки должны сохраняться атомарно.");

        File.WriteAllText(store.SettingsPath, "{broken-json", new UTF8Encoding(false));
        var recovered = store.Load();
        Assert(recovered.LeftIntervalMs == 17 && recovered.RightIntervalMs == 31,
            "Повреждённый JSON должен восстанавливаться из backup.");
        Assert(!string.IsNullOrWhiteSpace(store.LastRecoveryMessage),
            "Восстановление должно оставлять понятный статус.");
    }

    private static void TestLegacyIntervalMigration(string directory)
    {
        var importPath = Path.Combine(directory, "legacy.json");
        File.WriteAllText(
            importPath,
            """
            {
              "SchemaVersion": 3,
              "IntervalMs": 73,
              "OverlayOpacityPercent": 80
            }
            """,
            new UTF8Encoding(false));
        var store = new SettingsStore(directory, true);
        var migrated = store.Import(importPath);
        Assert(migrated.LeftIntervalMs == 73 && migrated.RightIntervalMs == 73,
            "Общий интервал 2.1 должен мигрировать в оба канала.");
    }

    private static void TestIndependentEngine()
    {
        var injector = new FakeInjector();
        using var engine = new ClickEngine(injector)
        {
            LeftIntervalMs = 4,
            RightIntervalMs = 11
        };
        Assert(engine.Start(ClickButton.Left), "LMB worker должен запускаться.");
        Assert(engine.Start(ClickButton.Right), "RMB worker должен запускаться независимо.");
        Thread.Sleep(150);
        engine.Stop(ClickButton.Left);
        var rightBefore = injector.RightClicks;
        Thread.Sleep(45);
        engine.Stop(ClickButton.Right);

        Assert(injector.LeftClicks > injector.RightClicks,
            "Независимый интервал 4 мс должен дать больше LMB, чем RMB с 11 мс.");
        Assert(injector.RightClicks > rightBefore,
            "RMB должен продолжать работу после остановки LMB.");
        Assert(injector.LeftReleases > 0 && injector.RightReleases > 0,
            "Остановка каждого worker должна гарантировать button-up.");

        engine.Start(ClickButton.Left);
        engine.Start(ClickButton.Right);
        Thread.Sleep(30);
        engine.PanicStop();
        Assert(!engine.IsRunning(ClickButton.Left) && !engine.IsRunning(ClickButton.Right),
            "Panic должен остановить оба worker.");
        Assert(injector.ReleaseAllCalls >= 2,
            "Panic должен отправлять button-up до и после ожидания worker.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeInjector : IInputInjector
    {
        private int _leftClicks;
        private int _rightClicks;
        private int _leftReleases;
        private int _rightReleases;
        private int _releaseAllCalls;

        internal int LeftClicks => Volatile.Read(ref _leftClicks);
        internal int RightClicks => Volatile.Read(ref _rightClicks);
        internal int LeftReleases => Volatile.Read(ref _leftReleases);
        internal int RightReleases => Volatile.Read(ref _rightReleases);
        internal int ReleaseAllCalls => Volatile.Read(ref _releaseAllCalls);

        public bool Click(ClickButton button)
        {
            if (button == ClickButton.Left)
            {
                Interlocked.Increment(ref _leftClicks);
            }
            else
            {
                Interlocked.Increment(ref _rightClicks);
            }

            return true;
        }

        public void Release(ClickButton button)
        {
            if (button == ClickButton.Left)
            {
                Interlocked.Increment(ref _leftReleases);
            }
            else
            {
                Interlocked.Increment(ref _rightReleases);
            }
        }

        public void ReleaseAll() => Interlocked.Increment(ref _releaseAllCalls);
    }
}
