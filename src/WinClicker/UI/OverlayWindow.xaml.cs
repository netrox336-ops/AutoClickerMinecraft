using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinClicker.Models;

namespace WinClicker.UI;

public partial class OverlayWindow : Window
{
    private AppSettings _settings;
    private IntPtr _handle;
    private bool _editMode;
    private bool _positioning;

    internal OverlayWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        SourceInitialized += Overlay_SourceInitialized;
        PreviewKeyDown += Overlay_PreviewKeyDown;
    }

    internal event EventHandler? CustomPositionChanged;
    internal event EventHandler? EditModeEnded;

    internal bool IsEditing => _editMode;

    internal void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        CompactPanel.Visibility = settings.OverlayDisplayMode == OverlayDisplayMode.Compact
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExtendedPanel.Visibility = settings.OverlayDisplayMode == OverlayDisplayMode.Extended
            ? Visibility.Visible
            : Visibility.Collapsed;
        var scale = settings.OverlayScalePercent / 100d;
        Width = (settings.OverlayDisplayMode == OverlayDisplayMode.Compact ? 340 : 430) * scale;
        Height = (settings.OverlayDisplayMode == OverlayDisplayMode.Compact ? 72 : 90) * scale;
        OverlayFrame.LayoutTransform = new ScaleTransform(scale, scale);
        OverlayFrame.Padding = settings.OverlayDisplayMode == OverlayDisplayMode.Compact
            ? new Thickness(15, 11, 15, 11)
            : new Thickness(17, 13, 17, 13);
        if (IsVisible)
        {
            Opacity = settings.OverlayOpacityPercent / 100d;
        }
        ApplyPosition();
    }

    internal void SetVisible(bool visible)
    {
        if (visible)
        {
            var wasHidden = !IsVisible;
            if (wasHidden)
            {
                Opacity = 0;
                Show();
                Opacity = 0;
            }

            var animation = new DoubleAnimation(
                Opacity,
                _settings.OverlayOpacityPercent / 100d,
                TimeSpan.FromMilliseconds(_settings.ReduceMotion ? 1 : 180));
            BeginAnimation(OpacityProperty, animation);
            ApplyPosition();
            EnsureTopmost();
            return;
        }

        if (!IsVisible)
        {
            return;
        }

        var fade = new DoubleAnimation(
            Opacity,
            0,
            TimeSpan.FromMilliseconds(_settings.ReduceMotion ? 1 : 140));
        fade.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fade);
    }

    internal void UpdateState(
        bool leftRunning,
        bool rightRunning,
        double leftCps,
        double rightCps,
        int leftInterval,
        int rightInterval)
    {
        var state = leftRunning && rightRunning
            ? "BOTH"
            : leftRunning
                ? "LMB"
                : rightRunning
                    ? "RMB"
                    : "OFF";
        var active = leftRunning || rightRunning;
        var activeBrush = active
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("TextMutedBrush");

        CompactStateText.Text = state;
        ExtendedStateText.Text = state;
        CompactStateText.Foreground = active ? (Brush)FindResource("TextPrimaryBrush") : (Brush)FindResource("TextMutedBrush");
        ExtendedStateText.Foreground = CompactStateText.Foreground;
        CompactPulse.Fill = activeBrush;
        ExtendedDot.Fill = activeBrush;

        CompactCpsText.Text = $"{Math.Round(leftCps + rightCps):N0} CPS";
        CompactIntervalText.Text = $"L {leftInterval} ms  •  R {rightInterval} ms";
        ExtendedLeftText.Text = $"LMB  {Math.Round(leftCps):N0} CPS  •  {leftInterval} ms";
        ExtendedRightText.Text = $"RMB  {Math.Round(rightCps):N0} CPS  •  {rightInterval} ms";

        if (active && !_settings.ReduceMotion)
        {
            var pulse = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(180))
            {
                AutoReverse = true
            };
            CompactPulse.BeginAnimation(OpacityProperty, pulse);
            ExtendedDot.BeginAnimation(OpacityProperty, pulse);
        }
    }

    internal void BeginEditMode()
    {
        if (!IsVisible)
        {
            Show();
        }

        _editMode = true;
        _settings.OverlayUseCustomPosition = true;
        Height += 32 * (_settings.OverlayScalePercent / 100d);
        EditBadge.Visibility = Visibility.Visible;
        OverlayFrame.BorderBrush = (Brush)FindResource("AccentBrush");
        Focusable = true;
        SetClickThrough(false);
        Activate();
        Focus();
    }

    internal void EndEditMode()
    {
        if (!_editMode)
        {
            return;
        }

        _editMode = false;
        EditBadge.Visibility = Visibility.Collapsed;
        OverlayFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(58, 66, 79));
        Focusable = false;
        SetClickThrough(true);
        ApplySettings(_settings);
        EditModeEnded?.Invoke(this, EventArgs.Empty);
    }

    internal void ApplyPosition()
    {
        if (_handle == IntPtr.Zero || _editMode || !_settings.OverlayEnabled)
        {
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            foreground = _handle;
        }

        var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(foreground);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96d;
        var width = Math.Max(1, (int)Math.Round(Width * scale));
        var height = Math.Max(1, (int)Math.Round(Height * scale));
        const int margin = 22;
        var bounds = monitorInfo.rcMonitor;

        int x;
        int y;
        if (_settings.OverlayUseCustomPosition)
        {
            x = bounds.left + (int)Math.Round(_settings.OverlayCustomX);
            y = bounds.top + (int)Math.Round(_settings.OverlayCustomY);
            x = Math.Clamp(x, bounds.left, bounds.right - width);
            y = Math.Clamp(y, bounds.top, bounds.bottom - height);
        }
        else
        {
            x = _settings.OverlayCorner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft
                ? bounds.left + margin
                : bounds.right - width - margin;
            y = _settings.OverlayCorner is OverlayCorner.TopLeft or OverlayCorner.TopRight
                ? bounds.top + margin
                : bounds.bottom - height - margin;
        }

        _positioning = true;
        NativeMethods.SetWindowPos(
            _handle,
            NativeMethods.HwndTopmost,
            x,
            y,
            width,
            height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _positioning = false;
    }

    internal void EnsureTopmost()
    {
        if (_handle == IntPtr.Zero || !IsVisible)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private void Overlay_SourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        SetClickThrough(true);
        ApplySettings(_settings);
    }

    private void SetClickThrough(bool enabled)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExLayered;
        if (enabled)
        {
            style |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        }
        else
        {
            style &= ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);
        }

        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, new IntPtr(style));
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw when the physical button was released between messages.
        }
    }

    private void Overlay_LocationChanged(object? sender, EventArgs e)
    {
        if (!_editMode || _positioning || _handle == IntPtr.Zero)
        {
            return;
        }

        if (!NativeMethods.GetWindowRect(_handle, out var rect))
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(_handle, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        _settings.OverlayCustomX = rect.left - monitorInfo.rcMonitor.left;
        _settings.OverlayCustomY = rect.top - monitorInfo.rcMonitor.top;
        CustomPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Overlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_editMode && e.Key == Key.Escape)
        {
            EndEditMode();
            e.Handled = true;
        }
    }
}
