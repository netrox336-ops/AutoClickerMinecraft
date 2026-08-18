using System.Windows;
using System.Windows.Threading;
using WinClicker.Services;
using WinClicker.UI;

namespace WinClicker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(value => string.Equals(value, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTestRunner.Run());
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        var startMinimized = e.Args.Any(value => string.Equals(value, "--minimized", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(startMinimized);
        MainWindow = window;
        SessionEnding += (_, _) => window.PrepareForSystemShutdown();
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (Current.MainWindow is MainWindow window)
        {
            window.EmergencyStopFromApp();
        }
        else
        {
            ClickEngine.EmergencyReleaseAll();
        }
        CrashLogService.Write(e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "Произошла непредвиденная ошибка. Все каналы остановлены. Диагностический файл сохранён локально.",
            "Auto Clicker",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        ClickEngine.EmergencyReleaseAll();
        if (e.ExceptionObject is Exception exception)
        {
            CrashLogService.Write(exception);
        }
    }
}
