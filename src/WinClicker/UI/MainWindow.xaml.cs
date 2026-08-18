using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using WinClicker.Models;
using WinClicker.Services;

namespace WinClicker.UI;

public partial class MainWindow : Window
{
    private readonly bool _startMinimizedArgument;
    private readonly SettingsStore _settingsStore;
    private readonly ClickEngine _engine;
    private readonly GlobalInputHook _hook;
    private readonly MinecraftBorderlessService _minecraft;
    private readonly UpdateService _updateService = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _minecraftTimer;
    private readonly Stopwatch _rateClock = Stopwatch.StartNew();

    private AppSettings _settings;
    private OverlayWindow? _overlay;
    private TrayService? _tray;
    private HotkeyCaptureDialog? _captureDialog;
    private HwndSource? _windowSource;
    private HotkeyAction? _testAction;
    private DateTime _testDeadline;
    private DateTime _lastBorderlessAttempt;
    private IntPtr _lastBorderlessWindow;
    private long _lastLeftClicks;
    private long _lastRightClicks;
    private long _lastRateTimestamp;
    private double _leftCps;
    private double _rightCps;
    private bool _updatingUi;
    private bool _allowClose;
    private bool _cleanedUp;
    private bool _borderlessPromptOpen;
    private int _toastGeneration;

    internal MainWindow(bool startMinimizedArgument)
    {
        _startMinimizedArgument = startMinimizedArgument;
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        ThemeManager.Apply(_settings.AccentTheme, _settings.WindowSurfaceOpacityPercent);

        _updatingUi = true;
        InitializeComponent();
        _updatingUi = false;

        _engine = new ClickEngine
        {
            LeftIntervalMs = _settings.LeftIntervalMs,
            RightIntervalMs = _settings.RightIntervalMs
        };
        _hook = new GlobalInputHook();
        _minecraft = new MinecraftBorderlessService();

        _engine.ChannelStateChanged += Engine_ChannelStateChanged;
        _hook.HotkeyChanged += Hook_HotkeyChanged;
        _hook.BindingCaptured += Hook_BindingCaptured;
        _hook.UpdateConfiguration(_settings);

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _uiTimer.Tick += UiTimer_Tick;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _settingsStore.Save(_settings);
        };

        _minecraftTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };
        _minecraftTimer.Tick += MinecraftTimer_Tick;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        ThemeManager.ApplyWindowBackdrop(this);
        _windowSource = (HwndSource)PresentationSource.FromVisual(this);
        _windowSource.AddHook(WindowMessageHook);
        NativeMethods.WTSRegisterSessionNotification(_windowSource.Handle, NativeMethods.NotifyForThisSession);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySettingsToUi();
        Navigate("Control");

        try
        {
            _hook.Start();
            UpdateHookStatus();
        }
        catch (Exception exception)
        {
            HookStatusDot.Fill = FindBrush("DangerBrush");
            HookStatusText.Text = "HOOK ERROR";
            HotkeyHookDot.Fill = FindBrush("DangerBrush");
            HotkeyHookText.Text = "HOOK НЕ РАБОТАЕТ";
            ShowToast(exception.Message);
        }

        _overlay = new OverlayWindow(_settings);
        _overlay.CustomPositionChanged += (_, _) => ScheduleSave();
        _overlay.EditModeEnded += (_, _) =>
        {
            OverlayEditButton.Content = "ПЕРЕТАЩИТЬ МЫШКОЙ";
            ScheduleSave();
        };
        _overlay.ApplySettings(_settings);
        _overlay.SetVisible(_settings.OverlayEnabled);

        _tray = new TrayService();
        _tray.OpenRequested += (_, _) => Dispatcher.Invoke(ShowFromTray);
        _tray.PanicRequested += (_, _) => Dispatcher.Invoke(PanicStop);
        _tray.ExitRequested += (_, _) => Dispatcher.Invoke(ExitApplication);

        _lastRateTimestamp = _rateClock.ElapsedTicks;
        _lastLeftClicks = _engine.GetDeliveredClicks(ClickButton.Left);
        _lastRightClicks = _engine.GetDeliveredClicks(ClickButton.Right);
        _uiTimer.Start();
        _minecraftTimer.Start();

        if (!string.IsNullOrWhiteSpace(_settingsStore.LastRecoveryMessage))
        {
            ShowToast(_settingsStore.LastRecoveryMessage!);
        }

        if (_startMinimizedArgument || _settings.StartMinimized)
        {
            await Dispatcher.InvokeAsync(HideToTray, DispatcherPriority.ApplicationIdle);
        }

        if (_settings.CheckUpdates)
        {
            await Task.Delay(1200);
            await CheckForUpdatesAsync(false);
        }
    }

    private IntPtr WindowMessageHook(
        IntPtr hWnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WmWtsSessionChange
            && wParam.ToInt32() == NativeMethods.WtsSessionLock
            && _settings.PauseOnSessionLock)
        {
            PanicStop();
        }

        return IntPtr.Zero;
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton { Tag: string page })
        {
            return;
        }

        Navigate(page);
    }

    private void Navigate(string page)
    {
        var pages = new Dictionary<string, ScrollViewer>
        {
            ["Control"] = ControlPage,
            ["Hotkeys"] = HotkeysPage,
            ["Overlay"] = OverlayPage,
            ["Settings"] = SettingsPage
        };

        foreach (var candidate in pages.Values)
        {
            candidate.Visibility = Visibility.Collapsed;
        }

        if (!pages.TryGetValue(page, out var selected))
        {
            selected = ControlPage;
            page = "Control";
        }

        selected.Visibility = Visibility.Visible;
        selected.ScrollToTop();
        if (!_settings.ReduceMotion)
        {
            selected.Opacity = 0;
            var transform = new TranslateTransform(14, 0);
            selected.RenderTransform = transform;
            selected.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(210))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
        else
        {
            selected.Opacity = 1;
            selected.RenderTransform = Transform.Identity;
        }

        (HeaderTitle.Text, HeaderSubtitle.Text) = page switch
        {
            "Hotkeys" => ("HOTKEY STUDIO", "Клавиатура и Mouse 3 / Mouse 4 / Mouse 5"),
            "Overlay" => ("GAME OVERLAY", "Минимальный HUD через Windows compositor"),
            "Settings" => ("НАСТРОЙКИ", "Дизайн, Windows, обновления и обслуживание"),
            _ => ("УПРАВЛЕНИЕ", "Два независимых канала • точный интервал")
        };
    }

    private void ApplySettingsToUi()
    {
        _updatingUi = true;
        try
        {
            ThemeManager.Apply(_settings.AccentTheme, _settings.WindowSurfaceOpacityPercent);
            LeftIntervalTextBox.Text = _settings.LeftIntervalMs.ToString();
            RightIntervalTextBox.Text = _settings.RightIntervalMs.ToString();
            LeftIntervalSlider.Value = _settings.LeftIntervalMs;
            RightIntervalSlider.Value = _settings.RightIntervalMs;

            LeftBindingText.Text = _settings.LeftHotkey.ToDisplayString();
            RightBindingText.Text = _settings.RightHotkey.ToDisplayString();
            PanicBindingText.Text = _settings.PanicHotkey.ToDisplayString();
            SidebarPanicKey.Text = _settings.PanicHotkey.ToDisplayString();
            PanicSummary.Text = _settings.PanicHotkey.ToDisplayString();
            SelectCombo(LeftModeCombo, _settings.LeftHotkeyMode.ToString());
            SelectCombo(RightModeCombo, _settings.RightHotkeyMode.ToString());

            OverlayEnabledToggle.IsChecked = _settings.OverlayEnabled;
            SelectCombo(OverlayDisplayCombo, _settings.OverlayDisplayMode.ToString());
            SelectCombo(OverlayCornerCombo, _settings.OverlayCorner.ToString());
            OverlayScaleSlider.Value = _settings.OverlayScalePercent;
            OverlayOpacitySlider.Value = _settings.OverlayOpacityPercent;
            OverlayScaleValue.Text = $"{_settings.OverlayScalePercent}%";
            OverlayOpacityValue.Text = $"{_settings.OverlayOpacityPercent}%";
            OverlayFollowMonitorToggle.IsChecked = _settings.OverlayFollowActiveMonitor;
            MinecraftBorderlessToggle.IsChecked = _settings.MinecraftBorderlessEnabled;

            WindowOpacitySlider.Value = _settings.WindowSurfaceOpacityPercent;
            WindowOpacityValue.Text = $"{_settings.WindowSurfaceOpacityPercent}%";
            ReduceMotionToggle.IsChecked = _settings.ReduceMotion;
            StartWithWindowsToggle.IsChecked = StartupService.IsEnabled();
            _settings.StartWithWindows = StartWithWindowsToggle.IsChecked == true;
            StartMinimizedToggle.IsChecked = _settings.StartMinimized;
            MinimizeToTrayToggle.IsChecked = _settings.MinimizeToTray;
            PauseOnLockToggle.IsChecked = _settings.PauseOnSessionLock;
            SelectCombo(CloseBehaviorCombo, _settings.CloseBehavior.ToString());

            CheckUpdatesToggle.IsChecked = _settings.CheckUpdates;
            QuietUpdateToggle.IsChecked = _settings.QuietUpdateNotification;
            BuildInfoText.Text = $"Windows 11 x64 • WPF • {(_settingsStore.IsPortable ? "Portable" : "Installed")} mode";

            _engine.LeftIntervalMs = _settings.LeftIntervalMs;
            _engine.RightIntervalMs = _settings.RightIntervalMs;
            _hook.UpdateConfiguration(_settings);
            UpdateBorderlessConsentText();
            RefreshChannelUi();
        }
        finally
        {
            _updatingUi = false;
        }

        _overlay?.ApplySettings(_settings);
        _overlay?.SetVisible(_settings.OverlayEnabled);
    }

    private void LeftIntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingUi || !int.TryParse(LeftIntervalTextBox.Text, out var interval))
        {
            return;
        }

        SetLeftInterval(interval, false);
    }

    private void RightIntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingUi || !int.TryParse(RightIntervalTextBox.Text, out var interval))
        {
            return;
        }

        SetRightInterval(interval, false);
    }

    private void LeftIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi)
        {
            return;
        }

        SetLeftInterval((int)Math.Round(e.NewValue), true);
    }

    private void RightIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi)
        {
            return;
        }

        SetRightInterval((int)Math.Round(e.NewValue), true);
    }

    private void IntervalTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _updatingUi = true;
        LeftIntervalTextBox.Text = _settings.LeftIntervalMs.ToString();
        RightIntervalTextBox.Text = _settings.RightIntervalMs.ToString();
        _updatingUi = false;
    }

    private void SetLeftInterval(int interval, bool updateText)
    {
        interval = Math.Clamp(interval, 1, 1000);
        _settings.LeftIntervalMs = interval;
        _engine.LeftIntervalMs = interval;
        _updatingUi = true;
        LeftIntervalSlider.Value = interval;
        if (updateText || LeftIntervalTextBox.Text != interval.ToString())
        {
            LeftIntervalTextBox.Text = interval.ToString();
            LeftIntervalTextBox.CaretIndex = LeftIntervalTextBox.Text.Length;
        }
        _updatingUi = false;
        ScheduleSave();
    }

    private void SetRightInterval(int interval, bool updateText)
    {
        interval = Math.Clamp(interval, 1, 1000);
        _settings.RightIntervalMs = interval;
        _engine.RightIntervalMs = interval;
        _updatingUi = true;
        RightIntervalSlider.Value = interval;
        if (updateText || RightIntervalTextBox.Text != interval.ToString())
        {
            RightIntervalTextBox.Text = interval.ToString();
            RightIntervalTextBox.CaretIndex = RightIntervalTextBox.Text.Length;
        }
        _updatingUi = false;
        ScheduleSave();
    }

    private void LeftPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && int.TryParse(value, out var interval))
        {
            SetLeftInterval(interval, true);
        }
    }

    private void RightPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && int.TryParse(value, out var interval))
        {
            SetRightInterval(interval, true);
        }
    }

    private void LeftManual_Click(object sender, RoutedEventArgs e) => _engine.Toggle(ClickButton.Left);

    private void RightManual_Click(object sender, RoutedEventArgs e) => _engine.Toggle(ClickButton.Right);

    private void Engine_ChannelStateChanged(object? sender, ClickChannelEventArgs e)
        => Dispatcher.BeginInvoke((Action)RefreshChannelUi);

    private void Hook_HotkeyChanged(object? sender, GlobalHotkeyEventArgs e)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            SetHotkeyPressedVisual(e.Action, e.Signal == HotkeySignal.Pressed);

            if (_testAction == e.Action && e.Signal == HotkeySignal.Pressed)
            {
                _testAction = null;
                ShowToast($"{GetActionName(e.Action)}: сигнал получен, hook работает");
                return;
            }

            if (e.Action == HotkeyAction.Panic)
            {
                if (e.Signal == HotkeySignal.Pressed)
                {
                    PanicStop();
                }
                return;
            }

            var button = e.Action == HotkeyAction.LeftClicker ? ClickButton.Left : ClickButton.Right;
            var mode = e.Action == HotkeyAction.LeftClicker ? _settings.LeftHotkeyMode : _settings.RightHotkeyMode;
            if (e.Signal == HotkeySignal.Pressed)
            {
                switch (mode)
                {
                    case HotkeyTriggerMode.Toggle:
                        _engine.Toggle(button);
                        break;
                    case HotkeyTriggerMode.Hold:
                        _engine.Start(button);
                        break;
                    case HotkeyTriggerMode.Press:
                        _engine.TriggerOnce(button);
                        break;
                }
            }
            else if (mode == HotkeyTriggerMode.Hold)
            {
                _engine.Stop(button);
            }
        }));
    }

    private void Hook_BindingCaptured(object? sender, HotkeyCaptureEventArgs e)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (_captureDialog is null)
            {
                return;
            }

            if (e.Cancelled || e.Binding is null)
            {
                _captureDialog.CancelCapture();
            }
            else
            {
                _captureDialog.Complete(e.Binding);
            }
        }));
    }

    private void CaptureLeft_Click(object sender, RoutedEventArgs e)
        => CaptureHotkey(HotkeyAction.LeftClicker, "Хоткей LMB", _settings.LeftHotkey);

    private void CaptureRight_Click(object sender, RoutedEventArgs e)
        => CaptureHotkey(HotkeyAction.RightClicker, "Хоткей RMB", _settings.RightHotkey);

    private void CapturePanic_Click(object sender, RoutedEventArgs e)
        => CaptureHotkey(HotkeyAction.Panic, "Panic Key", _settings.PanicHotkey);

    private void CaptureHotkey(HotkeyAction action, string title, HotkeyBinding current)
    {
        if (!_hook.IsHealthy)
        {
            ShowToast("Глобальный hook не подключён — сначала перезапустите приложение");
            return;
        }

        var dialog = new HotkeyCaptureDialog(title, current) { Owner = this };
        _captureDialog = dialog;
        _hook.CaptureMode = true;
        var accepted = dialog.ShowDialog() == true;
        _hook.CaptureMode = false;
        _captureDialog = null;

        if (!accepted || dialog.CapturedBinding is null)
        {
            return;
        }

        var binding = dialog.CapturedBinding;
        var conflict = action switch
        {
            HotkeyAction.LeftClicker when binding.Equals(_settings.RightHotkey) => "Этот хоткей уже управляет RMB.",
            HotkeyAction.LeftClicker when binding.Equals(_settings.PanicHotkey) => "Этот хоткей занят Panic Key.",
            HotkeyAction.RightClicker when binding.Equals(_settings.LeftHotkey) => "Этот хоткей уже управляет LMB.",
            HotkeyAction.RightClicker when binding.Equals(_settings.PanicHotkey) => "Этот хоткей занят Panic Key.",
            HotkeyAction.Panic when binding.Equals(_settings.LeftHotkey) => "Этот хоткей уже управляет LMB.",
            HotkeyAction.Panic when binding.Equals(_settings.RightHotkey) => "Этот хоткей уже управляет RMB.",
            _ => null
        };
        if (conflict is not null)
        {
            ShowToast(conflict);
            return;
        }

        switch (action)
        {
            case HotkeyAction.LeftClicker:
                _settings.LeftHotkey = binding;
                break;
            case HotkeyAction.RightClicker:
                _settings.RightHotkey = binding;
                break;
            case HotkeyAction.Panic:
                _settings.PanicHotkey = binding;
                break;
        }

        _hook.UpdateConfiguration(_settings);
        ApplySettingsToUi();
        ScheduleSave();
        ShowToast($"{GetActionName(action)}: {binding.ToDisplayString()}");
    }

    private void TestLeft_Click(object sender, RoutedEventArgs e) => BeginHotkeyTest(HotkeyAction.LeftClicker);
    private void TestRight_Click(object sender, RoutedEventArgs e) => BeginHotkeyTest(HotkeyAction.RightClicker);
    private void TestPanic_Click(object sender, RoutedEventArgs e) => BeginHotkeyTest(HotkeyAction.Panic);

    private void ResetLeftHotkey_Click(object sender, RoutedEventArgs e)
    {
        _engine.Stop(ClickButton.Left);
        _settings.LeftHotkey = HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse4);
        ResolveResetConflict(HotkeyAction.LeftClicker);
        ApplySettingsToUi();
        ScheduleSave();
    }

    private void ResetRightHotkey_Click(object sender, RoutedEventArgs e)
    {
        _engine.Stop(ClickButton.Right);
        _settings.RightHotkey = HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse5);
        ResolveResetConflict(HotkeyAction.RightClicker);
        ApplySettingsToUi();
        ScheduleSave();
    }

    private void ResetPanicHotkey_Click(object sender, RoutedEventArgs e)
    {
        _settings.PanicHotkey = HotkeyBinding.FromKey(VirtualKeys.F12);
        ResolveResetConflict(HotkeyAction.Panic);
        ApplySettingsToUi();
        ScheduleSave();
    }

    private void ResolveResetConflict(HotkeyAction action)
    {
        _settings.Normalize();
        _hook.UpdateConfiguration(_settings);
        ShowToast($"{GetActionName(action)}: стандартный хоткей восстановлен");
    }

    private void BeginHotkeyTest(HotkeyAction action)
    {
        _testAction = action;
        _testDeadline = DateTime.UtcNow.AddSeconds(8);
        ShowToast($"Тест: нажмите {GetBinding(action).ToDisplayString()}");
    }

    private void HotkeyMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        var previousLeft = _settings.LeftHotkeyMode;
        var previousRight = _settings.RightHotkeyMode;
        _settings.LeftHotkeyMode = GetComboEnum(LeftModeCombo, HotkeyTriggerMode.Hold);
        _settings.RightHotkeyMode = GetComboEnum(RightModeCombo, HotkeyTriggerMode.Hold);
        if (previousLeft != _settings.LeftHotkeyMode) _engine.Stop(ClickButton.Left);
        if (previousRight != _settings.RightHotkeyMode) _engine.Stop(ClickButton.Right);
        RefreshChannelUi();
        ScheduleSave();
    }

    private void SwapChannels_Click(object sender, RoutedEventArgs e)
    {
        PanicStop();
        (_settings.LeftHotkey, _settings.RightHotkey) = (_settings.RightHotkey, _settings.LeftHotkey);
        (_settings.LeftHotkeyMode, _settings.RightHotkeyMode) = (_settings.RightHotkeyMode, _settings.LeftHotkeyMode);
        (_settings.LeftIntervalMs, _settings.RightIntervalMs) = (_settings.RightIntervalMs, _settings.LeftIntervalMs);
        ApplySettingsToUi();
        ScheduleSave();
        ShowToast("Каналы LMB и RMB поменяны местами");
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        PanicStop();
        _settings.LeftHotkey = HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse4);
        _settings.RightHotkey = HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse5);
        _settings.PanicHotkey = HotkeyBinding.FromKey(VirtualKeys.F12);
        _settings.LeftHotkeyMode = HotkeyTriggerMode.Hold;
        _settings.RightHotkeyMode = HotkeyTriggerMode.Hold;
        ApplySettingsToUi();
        ScheduleSave();
        ShowToast("Стандартные хоткеи восстановлены");
    }

    private async void OverlayOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        var wasEnabled = _settings.OverlayEnabled;
        _settings.OverlayEnabled = OverlayEnabledToggle.IsChecked == true;
        _settings.OverlayDisplayMode = GetComboEnum(OverlayDisplayCombo, OverlayDisplayMode.Compact);
        _settings.OverlayCorner = GetComboEnum(OverlayCornerCombo, OverlayCorner.TopRight);
        _settings.OverlayScalePercent = (int)Math.Round(OverlayScaleSlider.Value);
        _settings.OverlayOpacityPercent = (int)Math.Round(OverlayOpacitySlider.Value);
        _settings.OverlayFollowActiveMonitor = OverlayFollowMonitorToggle.IsChecked == true;
        _settings.MinecraftBorderlessEnabled = MinecraftBorderlessToggle.IsChecked == true;
        if (ReferenceEquals(sender, OverlayCornerCombo))
        {
            _settings.OverlayUseCustomPosition = false;
        }

        OverlayScaleValue.Text = $"{_settings.OverlayScalePercent}%";
        OverlayOpacityValue.Text = $"{_settings.OverlayOpacityPercent}%";
        _overlay?.ApplySettings(_settings);
        _overlay?.SetVisible(_settings.OverlayEnabled);

        if ((wasEnabled && !_settings.OverlayEnabled) || !_settings.MinecraftBorderlessEnabled)
        {
            await _minecraft.RestoreAsync();
        }

        ScheduleSave();
    }

    private void OverlayEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_overlay is null)
        {
            return;
        }

        if (_overlay.IsEditing)
        {
            _overlay.EndEditMode();
            OverlayEditButton.Content = "ПЕРЕТАЩИТЬ МЫШКОЙ";
            return;
        }

        if (!_settings.OverlayEnabled)
        {
            _settings.OverlayEnabled = true;
            OverlayEnabledToggle.IsChecked = true;
        }

        _settings.OverlayUseCustomPosition = true;
        _overlay.ApplySettings(_settings);
        _overlay.BeginEditMode();
        OverlayEditButton.Content = "ГОТОВО";
        ShowToast("Перетащите overlay мышкой. Esc — завершить.");
    }

    private void OverlayResetPosition_Click(object sender, RoutedEventArgs e)
    {
        _settings.OverlayUseCustomPosition = false;
        _overlay?.ApplyPosition();
        ScheduleSave();
        ShowToast("Overlay возвращён в выбранный угол");
    }

    private void ResetBorderlessConsent_Click(object sender, RoutedEventArgs e)
    {
        _settings.MinecraftBorderlessConsent = BorderlessConsent.Unknown;
        UpdateBorderlessConsentText();
        ScheduleSave();
        ShowToast("При следующем fullscreen Minecraft разрешение будет запрошено снова");
    }

    private async void MinecraftTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settings.OverlayEnabled
            || !_settings.MinecraftBorderlessEnabled
            || _borderlessPromptOpen)
        {
            return;
        }

        var info = _minecraft.FindForegroundFullscreenMinecraft();
        if (info is null || _minecraft.IsManaged(info.Handle))
        {
            return;
        }

        if (_lastBorderlessWindow == info.Handle && DateTime.UtcNow - _lastBorderlessAttempt < TimeSpan.FromSeconds(8))
        {
            return;
        }

        if (_settings.MinecraftBorderlessConsent == BorderlessConsent.Declined)
        {
            return;
        }

        if (_settings.MinecraftBorderlessConsent == BorderlessConsent.Unknown)
        {
            _borderlessPromptOpen = true;
            if (!IsVisible)
            {
                ShowFromTray();
            }
            var allowed = ConfirmDialog.Show(
                this,
                "Minecraft fullscreen",
                "Обнаружен полноэкранный Minecraft. Перевести его в визуально идентичный borderless fullscreen, чтобы overlay работал через стандартный Windows compositor?\n\nРешение будет сохранено. Исходное состояние окна восстановится при отключении overlay или выходе.",
                "ВКЛЮЧИТЬ BORDERLESS",
                "НЕ РАЗРЕШАТЬ");
            _settings.MinecraftBorderlessConsent = allowed ? BorderlessConsent.Allowed : BorderlessConsent.Declined;
            _borderlessPromptOpen = false;
            UpdateBorderlessConsentText();
            ScheduleSave();
            if (!allowed)
            {
                return;
            }
        }

        _lastBorderlessWindow = info.Handle;
        _lastBorderlessAttempt = DateTime.UtcNow;
        var success = await _minecraft.EnableBorderlessAsync(info);
        if (success)
        {
            _overlay?.ApplyPosition();
            _overlay?.EnsureTopmost();
        }
        ShowToast(success
            ? "Minecraft переведён в borderless fullscreen"
            : "Не удалось изменить режим Minecraft — overlay продолжит работать где возможно");
    }

    private void WindowAppearance_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        _settings.WindowSurfaceOpacityPercent = (int)Math.Round(WindowOpacitySlider.Value);
        _settings.ReduceMotion = ReduceMotionToggle.IsChecked == true;
        WindowOpacityValue.Text = $"{_settings.WindowSurfaceOpacityPercent}%";
        ThemeManager.Apply(_settings.AccentTheme, _settings.WindowSurfaceOpacityPercent);
        _overlay?.ApplySettings(_settings);
        ScheduleSave();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }
            || !Enum.TryParse<AccentTheme>(tag, out var theme))
        {
            return;
        }

        _settings.AccentTheme = theme;
        ThemeManager.Apply(theme, _settings.WindowSurfaceOpacityPercent);
        _overlay?.ApplySettings(_settings);
        ScheduleSave();
        ShowToast($"Цветовая схема: {theme}");
    }

    private void SystemOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        var requestedStartup = StartWithWindowsToggle.IsChecked == true;
        if (requestedStartup != _settings.StartWithWindows)
        {
            if (!StartupService.SetEnabled(requestedStartup))
            {
                _updatingUi = true;
                StartWithWindowsToggle.IsChecked = _settings.StartWithWindows;
                _updatingUi = false;
                ShowToast("Windows не разрешила изменить автозапуск");
                return;
            }

            _settings.StartWithWindows = requestedStartup;
        }

        _settings.StartMinimized = StartMinimizedToggle.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayToggle.IsChecked == true;
        _settings.PauseOnSessionLock = PauseOnLockToggle.IsChecked == true;
        _settings.CloseBehavior = GetComboEnum(CloseBehaviorCombo, CloseBehavior.MinimizeToTray);
        ScheduleSave();
    }

    private void UpdateOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        _settings.CheckUpdates = CheckUpdatesToggle.IsChecked == true;
        _settings.QuietUpdateNotification = QuietUpdateToggle.IsChecked == true;
        UpdateStatusText.Text = $"Источник: github.com/{UpdateService.Repository}";
        ScheduleSave();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Проверяем GitHub Releases…";
        try
        {
            var update = await _updateService.CheckAsync();
            if (update is null)
            {
                UpdateStatusText.Text = "Установлена актуальная версия";
                if (interactive) ShowToast("Обновлений нет");
                return;
            }

            UpdateStatusText.Text = $"Доступна версия {update.Tag}";
            if (!interactive && _settings.QuietUpdateNotification)
            {
                _tray?.ShowInfo("Доступно обновление", $"Auto Clicker {update.Tag}");
                return;
            }

            if (ConfirmDialog.Show(
                    this,
                    "Доступно обновление",
                    $"{update.Name}\n\nОткрыть страницу GitHub Release в браузере?",
                    "ОТКРЫТЬ GITHUB",
                    "ПОЗЖЕ"))
            {
                Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
            }
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = "Не удалось проверить обновления";
            if (interactive) ShowToast(exception.Message);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Импорт настроек Auto Clicker",
            Filter = "Auto Clicker settings (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            PanicStop();
            _settings = _settingsStore.Import(dialog.FileName);
            ApplySettingsToUi();
            _settingsStore.Save(_settings);
            ShowToast("Настройки импортированы");
        }
        catch (Exception exception)
        {
            ShowToast($"Импорт отклонён: {exception.Message}");
        }
    }

    private void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт настроек Auto Clicker",
            FileName = "AutoClicker-settings.json",
            Filter = "JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _settingsStore.Export(dialog.FileName, _settings);
            ShowToast("Настройки экспортированы");
        }
        catch (Exception exception)
        {
            ShowToast($"Экспорт не выполнен: {exception.Message}");
        }
    }

    private void SaveDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить диагностический отчёт",
            FileName = $"AutoClicker-diagnostic-{DateTime.Now:yyyyMMdd-HHmm}.json",
            Filter = "JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            DiagnosticService.Save(
                dialog.FileName,
                _settings,
                _hook.IsHealthy,
                _settingsStore.IsPortable,
                _engine.GetDeliveredClicks(ClickButton.Left),
                _engine.GetDeliveredClicks(ClickButton.Right));
            ShowToast("Обезличенный диагностический отчёт сохранён");
        }
        catch (Exception exception)
        {
            ShowToast($"Не удалось сохранить отчёт: {exception.Message}");
        }
    }

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDialog.Show(
                this,
                "Сбросить настройки",
                "Все хоткеи, интервалы, overlay, темы и системные параметры будут возвращены к стандартным.",
                "СБРОСИТЬ",
                "ОТМЕНА"))
        {
            return;
        }

        PanicStop();
        await _minecraft.RestoreAsync();
        StartupService.SetEnabled(false);
        _settings = new AppSettings();
        _settings.Normalize();
        ApplySettingsToUi();
        _settingsStore.Save(_settings);
        ShowToast("Стандартная конфигурация восстановлена");
    }

    private void ResetStatistics_Click(object sender, RoutedEventArgs e)
    {
        _engine.ResetStatistics();
        _lastLeftClicks = 0;
        _lastRightClicks = 0;
        _leftCps = 0;
        _rightCps = 0;
        RefreshChannelUi();
        ShowToast("Статистика текущей сессии очищена");
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        ConfirmDialog.ShowInfo(
            this,
            "Auto Clicker 3.0.1",
            "Полноценный двухканальный автокликер для Windows 11.\n\nWPF • Per-Monitor DPI V2 • SendInput • WH_KEYBOARD_LL • WH_MOUSE_LL\n\nБез DLL-инъекции, вмешательства в рендер или механизмов обхода античита.");
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        _hook.ReconcilePhysicalState();
        if (_testAction is not null && DateTime.UtcNow > _testDeadline)
        {
            _testAction = null;
            ShowToast("Время теста хоткея истекло");
        }

        var now = _rateClock.ElapsedTicks;
        var elapsed = (now - _lastRateTimestamp) / (double)Stopwatch.Frequency;
        if (elapsed > 0)
        {
            var left = _engine.GetDeliveredClicks(ClickButton.Left);
            var right = _engine.GetDeliveredClicks(ClickButton.Right);
            var instantLeft = Math.Max(0, left - _lastLeftClicks) / elapsed;
            var instantRight = Math.Max(0, right - _lastRightClicks) / elapsed;
            _leftCps = _leftCps * 0.34 + instantLeft * 0.66;
            _rightCps = _rightCps * 0.34 + instantRight * 0.66;
            if (!_engine.IsRunning(ClickButton.Left) && instantLeft == 0) _leftCps *= 0.35;
            if (!_engine.IsRunning(ClickButton.Right) && instantRight == 0) _rightCps *= 0.35;
            if (_leftCps < 0.2) _leftCps = 0;
            if (_rightCps < 0.2) _rightCps = 0;
            _lastLeftClicks = left;
            _lastRightClicks = right;
            _lastRateTimestamp = now;
        }

        RefreshChannelUi();
        if (_overlay is not null && _settings.OverlayEnabled)
        {
            _overlay.UpdateState(
                _engine.IsRunning(ClickButton.Left),
                _engine.IsRunning(ClickButton.Right),
                _leftCps,
                _rightCps,
                _settings.LeftIntervalMs,
                _settings.RightIntervalMs);
            if (_settings.OverlayFollowActiveMonitor)
            {
                _overlay.ApplyPosition();
            }
            _overlay.EnsureTopmost();
        }
    }

    private void RefreshChannelUi()
    {
        if (!IsLoaded)
        {
            return;
        }

        var leftRunning = _engine.IsRunning(ClickButton.Left);
        var rightRunning = _engine.IsRunning(ClickButton.Right);
        LeftStateText.Text = leftRunning ? "ACTIVE" : "ГОТОВ";
        RightStateText.Text = rightRunning ? "ACTIVE" : "ГОТОВ";
        LeftStateText.Foreground = leftRunning ? FindBrush("SuccessBrush") : FindBrush("TextPrimaryBrush");
        RightStateText.Foreground = rightRunning ? FindBrush("SuccessBrush") : FindBrush("TextPrimaryBrush");
        LeftChannelCard.BorderBrush = leftRunning ? FindBrush("AccentBrush") : FindBrush("BorderSoftBrush");
        RightChannelCard.BorderBrush = rightRunning ? FindBrush("AccentBrush") : FindBrush("BorderSoftBrush");
        LeftManualButton.Content = leftRunning ? "ОСТАНОВИТЬ LMB" : "ЗАПУСТИТЬ LMB";
        RightManualButton.Content = rightRunning ? "ОСТАНОВИТЬ RMB" : "ЗАПУСТИТЬ RMB";

        AnimateText(LeftCpsText, $"{Math.Round(_leftCps):N0} CPS");
        AnimateText(RightCpsText, $"{Math.Round(_rightCps):N0} CPS");
        LeftClicksText.Text = $"{_engine.GetDeliveredClicks(ClickButton.Left):N0} доставлено";
        RightClicksText.Text = $"{_engine.GetDeliveredClicks(ClickButton.Right):N0} доставлено";
        LeftHotkeySummary.Text = $"{_settings.LeftHotkey.ToDisplayString()} • {_settings.LeftHotkeyMode.ToString().ToUpperInvariant()}";
        RightHotkeySummary.Text = $"{_settings.RightHotkey.ToDisplayString()} • {_settings.RightHotkeyMode.ToString().ToUpperInvariant()}";
    }

    private void SetHotkeyPressedVisual(HotkeyAction action, bool pressed)
    {
        var border = action switch
        {
            HotkeyAction.LeftClicker => LeftStudioStateBorder,
            HotkeyAction.RightClicker => RightStudioStateBorder,
            _ => PanicStudioStateBorder
        };
        border.BorderBrush = pressed ? FindBrush("AccentBrush") : action == HotkeyAction.Panic ? FindBrush("DangerBrush") : FindBrush("BorderBrush");
        border.Background = pressed ? FindBrush("AccentMutedBrush") : action == HotkeyAction.Panic ? new SolidColorBrush(Color.FromRgb(24, 17, 19)) : FindBrush("SurfaceRaisedBrush");

        var summary = action == HotkeyAction.LeftClicker
            ? LeftHotkeyStateBorder
            : action == HotkeyAction.RightClicker
                ? RightHotkeyStateBorder
                : null;
        if (summary is not null)
        {
            summary.BorderBrush = pressed ? FindBrush("AccentBrush") : FindBrush("BorderBrush");
            summary.Background = pressed ? FindBrush("AccentMutedBrush") : FindBrush("SurfaceRaisedBrush");
        }
    }

    private void Panic_Click(object sender, RoutedEventArgs e) => PanicStop();

    private void PanicStop()
    {
        _hook.ClearActiveState();
        _engine.PanicStop();
        SetHotkeyPressedVisual(HotkeyAction.LeftClicker, false);
        SetHotkeyPressedVisual(HotkeyAction.RightClicker, false);
        SetHotkeyPressedVisual(HotkeyAction.Panic, false);
        RefreshChannelUi();
        ShowToast("Emergency Stop: оба канала остановлены");
    }

    internal void EmergencyStopFromApp()
    {
        _hook.ClearActiveState();
        _engine.PanicStop();
    }

    internal void PrepareForSystemShutdown()
    {
        _allowClose = true;
        _hook.ClearActiveState();
        _engine.PanicStop();
    }

    private void UpdateHookStatus()
    {
        var healthy = _hook.IsHealthy;
        HookStatusDot.Fill = healthy ? FindBrush("SuccessBrush") : FindBrush("DangerBrush");
        HookStatusText.Text = healthy ? "HOOK READY" : "HOOK ERROR";
        HotkeyHookDot.Fill = HookStatusDot.Fill;
        HotkeyHookText.Text = healthy ? "ГЛОБАЛЬНЫЙ HOOK РАБОТАЕТ" : "HOOK НЕ РАБОТАЕТ";
    }

    private void UpdateBorderlessConsentText()
    {
        BorderlessConsentText.Text = _settings.MinecraftBorderlessConsent switch
        {
            BorderlessConsent.Allowed => "Разрешено. Fullscreen Minecraft будет автоматически переведён в borderless.",
            BorderlessConsent.Declined => "Запрещено. Режим Minecraft изменяться не будет.",
            _ => "При первом обнаружении fullscreen приложение спросит разрешение."
        };
    }

    private void AnimateText(TextBlock target, string value)
    {
        if (target.Text == value)
        {
            return;
        }

        target.Text = value;
        if (!_settings.ReduceMotion)
        {
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(0.58, 1, TimeSpan.FromMilliseconds(110)));
        }
    }

    private async void ShowToast(string message)
    {
        var generation = ++_toastGeneration;
        ToastText.Text = message;
        Toast.BeginAnimation(OpacityProperty, null);
        Toast.Opacity = 1;
        await Task.Delay(2300);
        if (generation != _toastGeneration)
        {
            return;
        }

        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(_settings.ReduceMotion ? 1 : 180)));
    }

    private void ScheduleSave()
    {
        if (_updatingUi)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static void SelectCombo(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private static T GetComboEnum<T>(ComboBox combo, T fallback) where T : struct, Enum
    {
        return combo.SelectedItem is ComboBoxItem item
               && Enum.TryParse<T>(item.Tag?.ToString(), true, out var value)
            ? value
            : fallback;
    }

    private HotkeyBinding GetBinding(HotkeyAction action) => action switch
    {
        HotkeyAction.LeftClicker => _settings.LeftHotkey,
        HotkeyAction.RightClicker => _settings.RightHotkey,
        _ => _settings.PanicHotkey
    };

    private static string GetActionName(HotkeyAction action) => action switch
    {
        HotkeyAction.LeftClicker => "LMB",
        HotkeyAction.RightClicker => "RMB",
        _ => "Panic Key"
    };

    private Brush FindBrush(string key) => (Brush)FindResource(key);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // The physical button can be released between the title-bar messages.
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        Hide();
        _tray?.ShowInfo("Auto Clicker", "Приложение продолжает работать в системном трее.");
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            if (_settings.CloseBehavior == CloseBehavior.MinimizeToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            if (_settings.CloseBehavior == CloseBehavior.Ask
                && !ConfirmDialog.Show(
                    this,
                    "Завершить Auto Clicker",
                    "Выйти из приложения? Оба канала будут остановлены, а окно Minecraft восстановлено.",
                    "ВЫЙТИ",
                    "В ТРЕЙ"))
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _allowClose = true;
        }

        Cleanup();
    }

    private void Cleanup()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        _uiTimer.Stop();
        _saveTimer.Stop();
        _minecraftTimer.Stop();
        _settingsStore.Save(_settings);
        _engine.PanicStop();
        _overlay?.Close();
        _minecraft.Dispose();
        _hook.Dispose();
        _engine.Dispose();
        _tray?.Dispose();

        if (_windowSource is not null)
        {
            NativeMethods.WTSUnRegisterSessionNotification(_windowSource.Handle);
            _windowSource.RemoveHook(WindowMessageHook);
        }
    }
}
