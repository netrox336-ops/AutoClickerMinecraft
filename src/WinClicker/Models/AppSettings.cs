using System.Text.Json.Serialization;

namespace WinClicker.Models;

internal enum ClickButton
{
    Left,
    Right
}

internal enum HotkeyTriggerMode
{
    Toggle,
    Hold,
    Press
}

internal enum HotkeyMouseButton
{
    None,
    Middle,
    Mouse4,
    Mouse5
}

internal enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

internal enum OverlayDisplayMode
{
    Compact,
    Extended
}

internal enum AccentTheme
{
    Red,
    Blue,
    Purple,
    Emerald,
    Amber
}

internal enum CloseBehavior
{
    MinimizeToTray,
    Exit,
    Ask
}

internal enum BorderlessConsent
{
    Unknown,
    Allowed,
    Declined
}

internal static class VirtualKeys
{
    internal const int Back = 0x08;
    internal const int Tab = 0x09;
    internal const int Enter = 0x0D;
    internal const int Shift = 0x10;
    internal const int Control = 0x11;
    internal const int Alt = 0x12;
    internal const int Pause = 0x13;
    internal const int CapsLock = 0x14;
    internal const int Escape = 0x1B;
    internal const int Space = 0x20;
    internal const int PageUp = 0x21;
    internal const int PageDown = 0x22;
    internal const int End = 0x23;
    internal const int Home = 0x24;
    internal const int Left = 0x25;
    internal const int Up = 0x26;
    internal const int Right = 0x27;
    internal const int Down = 0x28;
    internal const int Insert = 0x2D;
    internal const int Delete = 0x2E;
    internal const int LWin = 0x5B;
    internal const int RWin = 0x5C;
    internal const int NumPad0 = 0x60;
    internal const int NumPad9 = 0x69;
    internal const int F1 = 0x70;
    internal const int F12 = 0x7B;
    internal const int LShift = 0xA0;
    internal const int RShift = 0xA1;
    internal const int LControl = 0xA2;
    internal const int RControl = 0xA3;
    internal const int LAlt = 0xA4;
    internal const int RAlt = 0xA5;
}

internal sealed class HotkeyBinding : IEquatable<HotkeyBinding>
{
    public int VirtualKey { get; set; }
    public HotkeyMouseButton MouseButton { get; set; }
    public bool Control { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }

    [JsonIgnore]
    public bool IsMouse => MouseButton != HotkeyMouseButton.None;

    [JsonIgnore]
    public bool IsDisabled => !IsMouse && VirtualKey <= 0;

    [JsonIgnore]
    public int ModifierCount => (Control ? 1 : 0) + (Alt ? 1 : 0) + (Shift ? 1 : 0) + (Win ? 1 : 0);

    public static HotkeyBinding Disabled() => new();

    public static HotkeyBinding FromKey(
        int virtualKey,
        bool control = false,
        bool alt = false,
        bool shift = false,
        bool win = false)
    {
        return new HotkeyBinding
        {
            VirtualKey = virtualKey,
            Control = control,
            Alt = alt,
            Shift = shift,
            Win = win
        };
    }

    public static HotkeyBinding FromMouse(
        HotkeyMouseButton button,
        bool control = false,
        bool alt = false,
        bool shift = false,
        bool win = false)
    {
        return new HotkeyBinding
        {
            MouseButton = button,
            Control = control,
            Alt = alt,
            Shift = shift,
            Win = win
        };
    }

    public HotkeyBinding Clone() => new()
    {
        VirtualKey = VirtualKey,
        MouseButton = MouseButton,
        Control = Control,
        Alt = Alt,
        Shift = Shift,
        Win = Win
    };

    public void Normalize()
    {
        if (IsMouse)
        {
            if (!Enum.IsDefined(MouseButton))
            {
                Reset();
            }
            else
            {
                VirtualKey = 0;
            }

            return;
        }

        if (VirtualKey is < 1 or > 255 || IsModifierKey(VirtualKey))
        {
            Reset();
        }
    }

    public bool MatchesKeyboard(
        int virtualKey,
        bool control,
        bool alt,
        bool shift,
        bool win,
        bool ignoreExtraModifiers)
    {
        return !IsMouse
               && !IsDisabled
               && VirtualKey == virtualKey
               && ModifiersMatch(control, alt, shift, win, ignoreExtraModifiers);
    }

    public bool MatchesMouse(
        HotkeyMouseButton button,
        bool control,
        bool alt,
        bool shift,
        bool win,
        bool ignoreExtraModifiers)
    {
        return IsMouse
               && MouseButton == button
               && ModifiersMatch(control, alt, shift, win, ignoreExtraModifiers);
    }

    public string ToDisplayString()
    {
        if (IsDisabled)
        {
            return "Не назначено";
        }

        var parts = new List<string>(5);
        if (Control) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(IsMouse ? GetMouseButtonName(MouseButton) : GetKeyName(VirtualKey));
        return string.Join(" + ", parts);
    }

    public bool Equals(HotkeyBinding? other)
    {
        return other is not null
               && VirtualKey == other.VirtualKey
               && MouseButton == other.MouseButton
               && Control == other.Control
               && Alt == other.Alt
               && Shift == other.Shift
               && Win == other.Win;
    }

    public override bool Equals(object? obj) => Equals(obj as HotkeyBinding);

    public override int GetHashCode() => HashCode.Combine(VirtualKey, MouseButton, Control, Alt, Shift, Win);

    internal static bool IsModifierKey(int key)
    {
        return key is VirtualKeys.Shift or VirtualKeys.Control or VirtualKeys.Alt
            or VirtualKeys.LShift or VirtualKeys.RShift
            or VirtualKeys.LControl or VirtualKeys.RControl
            or VirtualKeys.LAlt or VirtualKeys.RAlt
            or VirtualKeys.LWin or VirtualKeys.RWin;
    }

    private bool ModifiersMatch(bool control, bool alt, bool shift, bool win, bool ignoreExtraModifiers)
    {
        if (ignoreExtraModifiers)
        {
            return (!Control || control)
                   && (!Alt || alt)
                   && (!Shift || shift)
                   && (!Win || win);
        }

        return Control == control && Alt == alt && Shift == shift && Win == win;
    }

    private void Reset()
    {
        VirtualKey = 0;
        MouseButton = HotkeyMouseButton.None;
        Control = false;
        Alt = false;
        Shift = false;
        Win = false;
    }

    private static string GetMouseButtonName(HotkeyMouseButton button) => button switch
    {
        HotkeyMouseButton.Middle => "Mouse 3",
        HotkeyMouseButton.Mouse4 => "Mouse 4",
        HotkeyMouseButton.Mouse5 => "Mouse 5",
        _ => "Mouse"
    };

    private static string GetKeyName(int key)
    {
        if (key is >= 0x30 and <= 0x39 || key is >= 0x41 and <= 0x5A)
        {
            return ((char)key).ToString();
        }

        if (key is >= VirtualKeys.NumPad0 and <= VirtualKeys.NumPad9)
        {
            return $"Num {key - VirtualKeys.NumPad0}";
        }

        if (key is >= VirtualKeys.F1 and <= VirtualKeys.F12)
        {
            return $"F{key - VirtualKeys.F1 + 1}";
        }

        return key switch
        {
            VirtualKeys.Back => "Backspace",
            VirtualKeys.Tab => "Tab",
            VirtualKeys.Enter => "Enter",
            VirtualKeys.Escape => "Esc",
            VirtualKeys.Space => "Space",
            VirtualKeys.PageUp => "Page Up",
            VirtualKeys.PageDown => "Page Down",
            VirtualKeys.End => "End",
            VirtualKeys.Home => "Home",
            VirtualKeys.Left => "Left",
            VirtualKeys.Up => "Up",
            VirtualKeys.Right => "Right",
            VirtualKeys.Down => "Down",
            VirtualKeys.Insert => "Insert",
            VirtualKeys.Delete => "Delete",
            _ => $"VK {key:X2}"
        };
    }
}

internal sealed class AppSettings
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public int LeftIntervalMs { get; set; } = 50;
    public int RightIntervalMs { get; set; } = 50;

    public HotkeyBinding LeftHotkey { get; set; } = HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse4);
    public HotkeyTriggerMode LeftHotkeyMode { get; set; } = HotkeyTriggerMode.Hold;

    public HotkeyBinding RightHotkey { get; set; } = HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse5);
    public HotkeyTriggerMode RightHotkeyMode { get; set; } = HotkeyTriggerMode.Hold;

    public HotkeyBinding PanicHotkey { get; set; } = HotkeyBinding.FromKey(VirtualKeys.F12);
    public bool IgnoreExtraModifiers { get; set; } = true;

    public bool OverlayEnabled { get; set; }
    public OverlayCorner OverlayCorner { get; set; } = global::WinClicker.Models.OverlayCorner.TopRight;
    public OverlayDisplayMode OverlayDisplayMode { get; set; } = global::WinClicker.Models.OverlayDisplayMode.Compact;
    public int OverlayOpacityPercent { get; set; } = 84;
    public int OverlayScalePercent { get; set; } = 100;
    public bool OverlayUseCustomPosition { get; set; }
    public double OverlayCustomX { get; set; } = 24;
    public double OverlayCustomY { get; set; } = 24;
    public bool OverlayFollowActiveMonitor { get; set; } = true;

    public bool MinecraftBorderlessEnabled { get; set; } = true;
    public BorderlessConsent MinecraftBorderlessConsent { get; set; } = global::WinClicker.Models.BorderlessConsent.Unknown;

    public AccentTheme AccentTheme { get; set; } = global::WinClicker.Models.AccentTheme.Red;
    public int WindowSurfaceOpacityPercent { get; set; } = 94;
    public bool ReduceMotion { get; set; }

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool PauseOnSessionLock { get; set; } = true;
    public CloseBehavior CloseBehavior { get; set; } = global::WinClicker.Models.CloseBehavior.MinimizeToTray;

    public bool CheckUpdates { get; set; } = true;
    public bool QuietUpdateNotification { get; set; } = true;

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        LeftIntervalMs = Math.Clamp(LeftIntervalMs, 1, 1000);
        RightIntervalMs = Math.Clamp(RightIntervalMs, 1, 1000);
        OverlayOpacityPercent = Math.Clamp(OverlayOpacityPercent, 35, 100);
        OverlayScalePercent = Math.Clamp(OverlayScalePercent, 70, 160);
        OverlayCustomX = Math.Clamp(OverlayCustomX, -10000, 10000);
        OverlayCustomY = Math.Clamp(OverlayCustomY, -10000, 10000);
        WindowSurfaceOpacityPercent = Math.Clamp(WindowSurfaceOpacityPercent, 72, 100);

        if (!Enum.IsDefined(LeftHotkeyMode)) LeftHotkeyMode = HotkeyTriggerMode.Hold;
        if (!Enum.IsDefined(RightHotkeyMode)) RightHotkeyMode = HotkeyTriggerMode.Hold;
        if (!Enum.IsDefined(OverlayCorner)) OverlayCorner = global::WinClicker.Models.OverlayCorner.TopRight;
        if (!Enum.IsDefined(OverlayDisplayMode)) OverlayDisplayMode = global::WinClicker.Models.OverlayDisplayMode.Compact;
        if (!Enum.IsDefined(MinecraftBorderlessConsent)) MinecraftBorderlessConsent = global::WinClicker.Models.BorderlessConsent.Unknown;
        if (!Enum.IsDefined(AccentTheme)) AccentTheme = global::WinClicker.Models.AccentTheme.Red;
        if (!Enum.IsDefined(CloseBehavior)) CloseBehavior = global::WinClicker.Models.CloseBehavior.MinimizeToTray;

        LeftHotkey ??= HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse4);
        RightHotkey ??= HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse5);
        PanicHotkey ??= HotkeyBinding.FromKey(VirtualKeys.F12);
        LeftHotkey.Normalize();
        RightHotkey.Normalize();
        PanicHotkey.Normalize();

        if (PanicHotkey.IsDisabled)
        {
            PanicHotkey = HotkeyBinding.FromKey(VirtualKeys.F12);
        }

        ResolveUnsafeConflicts();
    }

    public IReadOnlyList<string> GetHotkeyConflicts()
    {
        var conflicts = new List<string>();
        if (!LeftHotkey.IsDisabled && LeftHotkey.Equals(RightHotkey))
        {
            conflicts.Add("LMB и RMB используют один хоткей");
        }

        if (!LeftHotkey.IsDisabled && LeftHotkey.Equals(PanicHotkey))
        {
            conflicts.Add("LMB конфликтует с Panic Key");
        }

        if (!RightHotkey.IsDisabled && RightHotkey.Equals(PanicHotkey))
        {
            conflicts.Add("RMB конфликтует с Panic Key");
        }

        return conflicts;
    }

    private void ResolveUnsafeConflicts()
    {
        if (!LeftHotkey.IsDisabled && LeftHotkey.Equals(PanicHotkey))
        {
            LeftHotkey = FirstAvailable(
                [
                    HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse4),
                    HotkeyBinding.FromKey(VirtualKeys.F1 + 5),
                    HotkeyBinding.FromKey(VirtualKeys.F1 + 7)
                ],
                PanicHotkey);
        }

        if (!RightHotkey.IsDisabled && (RightHotkey.Equals(PanicHotkey) || RightHotkey.Equals(LeftHotkey)))
        {
            RightHotkey = FirstAvailable(
                [
                    HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse5),
                    HotkeyBinding.FromKey(VirtualKeys.F1 + 6),
                    HotkeyBinding.FromKey(VirtualKeys.F1 + 8)
                ],
                PanicHotkey,
                LeftHotkey);
        }

        if (RightHotkey.Equals(PanicHotkey) || RightHotkey.Equals(LeftHotkey))
        {
            RightHotkey = HotkeyBinding.FromKey(VirtualKeys.F1 + 6);
        }
    }

    private static HotkeyBinding FirstAvailable(
        IEnumerable<HotkeyBinding> candidates,
        params HotkeyBinding[] unavailable)
    {
        return candidates.First(candidate => unavailable.All(value => !candidate.Equals(value)));
    }
}
