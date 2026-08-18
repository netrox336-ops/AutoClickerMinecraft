using System.ComponentModel;
using System.Runtime.InteropServices;
using WinClicker.Models;

namespace WinClicker.Services;

internal enum HotkeyAction
{
    LeftClicker,
    RightClicker,
    Panic
}

internal enum HotkeySignal
{
    Pressed,
    Released
}

internal sealed class GlobalHotkeyEventArgs : EventArgs
{
    internal GlobalHotkeyEventArgs(HotkeyAction action, HotkeySignal signal)
    {
        Action = action;
        Signal = signal;
    }

    internal HotkeyAction Action { get; }
    internal HotkeySignal Signal { get; }
}

internal sealed class HotkeyCaptureEventArgs : EventArgs
{
    internal HotkeyCaptureEventArgs(HotkeyBinding? binding, bool cancelled)
    {
        Binding = binding;
        Cancelled = cancelled;
    }

    internal HotkeyBinding? Binding { get; }
    internal bool Cancelled { get; }
}

internal sealed record HookConfiguration(
    HotkeyBinding Left,
    HotkeyBinding Right,
    HotkeyBinding Panic,
    bool IgnoreExtraModifiers);

internal sealed class GlobalInputHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardCallback;
    private readonly NativeMethods.LowLevelMouseProc _mouseCallback;
    private readonly HashSet<int> _pressedKeys = [];
    private readonly HashSet<int> _suppressedKeys = [];
    private readonly Dictionary<int, HotkeyAction> _activeKeyboardActions = [];
    private readonly HashSet<HotkeyMouseButton> _pressedMouseButtons = [];
    private readonly HashSet<HotkeyMouseButton> _suppressedMouseButtons = [];
    private readonly Dictionary<HotkeyMouseButton, HotkeyAction> _activeMouseActions = [];
    private IntPtr _keyboardHookHandle;
    private IntPtr _mouseHookHandle;
    private HookConfiguration _configuration = DefaultConfiguration();
    private bool _captureMode;
    private bool _disposed;

    internal GlobalInputHook()
    {
        _keyboardCallback = KeyboardHookCallback;
        _mouseCallback = MouseHookCallback;
    }

    internal event EventHandler<GlobalHotkeyEventArgs>? HotkeyChanged;
    internal event EventHandler<HotkeyCaptureEventArgs>? BindingCaptured;

    internal bool IsHealthy => _keyboardHookHandle != IntPtr.Zero && _mouseHookHandle != IntPtr.Zero;

    internal bool CaptureMode
    {
        get => _captureMode;
        set
        {
            _captureMode = value;
            _activeKeyboardActions.Clear();
            _activeMouseActions.Clear();
        }
    }

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsHealthy)
        {
            return;
        }

        var moduleHandle = NativeMethods.GetModuleHandle(null);
        _keyboardHookHandle = NativeMethods.SetWindowsKeyboardHookEx(
            NativeMethods.WhKeyboardLl,
            _keyboardCallback,
            moduleHandle,
            0);
        if (_keyboardHookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось подключить клавиатурный hook.");
        }

        _mouseHookHandle = NativeMethods.SetWindowsMouseHookEx(
            NativeMethods.WhMouseLl,
            _mouseCallback,
            moduleHandle,
            0);
        if (_mouseHookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
            throw new Win32Exception(error, "Не удалось подключить mouse hook.");
        }
    }

    internal void UpdateConfiguration(AppSettings settings)
    {
        _configuration = new HookConfiguration(
            settings.LeftHotkey.Clone(),
            settings.RightHotkey.Clone(),
            settings.PanicHotkey.Clone(),
            settings.IgnoreExtraModifiers);
    }

    internal void ClearActiveState()
    {
        _activeKeyboardActions.Clear();
        _activeMouseActions.Clear();
    }

    internal void ReconcilePhysicalState()
    {
        if (_disposed || _captureMode)
        {
            return;
        }

        foreach (var pair in _activeKeyboardActions.ToArray())
        {
            if (!NativeMethods.IsKeyDown(pair.Key) || !RequiredModifiersRemainDown(GetBinding(pair.Value)))
            {
                _activeKeyboardActions.Remove(pair.Key);
                _pressedKeys.Remove(pair.Key);
                RaiseHotkey(pair.Value, HotkeySignal.Released);
            }
        }

        foreach (var pair in _activeMouseActions.ToArray())
        {
            if (!NativeMethods.IsKeyDown(GetMouseVirtualKey(pair.Key))
                || !RequiredModifiersRemainDown(GetBinding(pair.Value)))
            {
                _activeMouseActions.Remove(pair.Key);
                _pressedMouseButtons.Remove(pair.Key);
                RaiseHotkey(pair.Value, HotkeySignal.Released);
            }
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NextKeyboard(nCode, wParam, lParam);
        }

        var message = unchecked((int)wParam.ToInt64());
        var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        var isUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
        if (!isDown && !isUp)
        {
            return NextKeyboard(nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
        if ((data.flags & NativeMethods.LlkhfInjected) != 0
            || data.dwExtraInfo == NativeMethods.AutoClickerInputMarker)
        {
            return NextKeyboard(nCode, wParam, lParam);
        }

        var key = unchecked((int)data.vkCode);
        if (isUp)
        {
            _pressedKeys.Remove(key);

            if (_activeKeyboardActions.Remove(key, out var releasedAction))
            {
                RaiseHotkey(releasedAction, HotkeySignal.Released);
            }

            ReleaseActionsWithMissingModifiers();
            return _suppressedKeys.Remove(key) ? new IntPtr(1) : NextKeyboard(nCode, wParam, lParam);
        }

        if (!_pressedKeys.Add(key))
        {
            return _suppressedKeys.Contains(key) ? new IntPtr(1) : NextKeyboard(nCode, wParam, lParam);
        }

        GetModifiers(out var control, out var alt, out var shift, out var win);

        if (_captureMode)
        {
            _suppressedKeys.Add(key);
            if (key == VirtualKeys.Escape)
            {
                RaiseCapture(null, true);
            }
            else if (!HotkeyBinding.IsModifierKey(key))
            {
                RaiseCapture(HotkeyBinding.FromKey(key, control, alt, shift, win), false);
            }

            return new IntPtr(1);
        }

        var match = SelectKeyboardAction(_configuration, key, control, alt, shift, win);
        if (match is null)
        {
            return NextKeyboard(nCode, wParam, lParam);
        }

        _activeKeyboardActions[key] = match.Value;
        RaiseHotkey(match.Value, HotkeySignal.Pressed);
        // Runtime hotkeys are observation-only in 3.0.1. Mouse/keyboard events
        // always continue into the game; only the explicit capture dialog swallows
        // input while a new binding is being recorded.
        return NextKeyboard(nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NextMouse(nCode, wParam, lParam);
        }

        var message = unchecked((int)wParam.ToInt64());
        var data = Marshal.PtrToStructure<NativeMethods.MsLlHookStruct>(lParam);
        if ((data.flags & NativeMethods.LlMhfInjected) != 0
            || data.dwExtraInfo == NativeMethods.AutoClickerInputMarker
            || !TryGetMouseButton(message, data.mouseData, out var button, out var isDown, out var isUp))
        {
            return NextMouse(nCode, wParam, lParam);
        }

        if (isUp)
        {
            _pressedMouseButtons.Remove(button);

            if (_activeMouseActions.Remove(button, out var releasedAction))
            {
                RaiseHotkey(releasedAction, HotkeySignal.Released);
            }

            return _suppressedMouseButtons.Remove(button) ? new IntPtr(1) : NextMouse(nCode, wParam, lParam);
        }

        if (!isDown || !_pressedMouseButtons.Add(button))
        {
            return _suppressedMouseButtons.Contains(button) ? new IntPtr(1) : NextMouse(nCode, wParam, lParam);
        }

        GetModifiers(out var control, out var alt, out var shift, out var win);

        if (_captureMode)
        {
            _suppressedMouseButtons.Add(button);
            RaiseCapture(HotkeyBinding.FromMouse(button, control, alt, shift, win), false);
            return new IntPtr(1);
        }

        var match = SelectMouseAction(_configuration, button, control, alt, shift, win);
        if (match is null)
        {
            return NextMouse(nCode, wParam, lParam);
        }

        _activeMouseActions[button] = match.Value;
        RaiseHotkey(match.Value, HotkeySignal.Pressed);
        return NextMouse(nCode, wParam, lParam);
    }

    private void ReleaseActionsWithMissingModifiers()
    {
        foreach (var pair in _activeKeyboardActions.ToArray())
        {
            if (!RequiredModifiersRemainDown(GetBinding(pair.Value)))
            {
                _activeKeyboardActions.Remove(pair.Key);
                RaiseHotkey(pair.Value, HotkeySignal.Released);
            }
        }

        foreach (var pair in _activeMouseActions.ToArray())
        {
            if (!RequiredModifiersRemainDown(GetBinding(pair.Value)))
            {
                _activeMouseActions.Remove(pair.Key);
                RaiseHotkey(pair.Value, HotkeySignal.Released);
            }
        }
    }

    private static HotkeyAction? SelectKeyboardAction(
        HookConfiguration configuration,
        int key,
        bool control,
        bool alt,
        bool shift,
        bool win)
    {
        HotkeyAction? best = null;
        var modifiers = -1;
        Consider(configuration.Panic.MatchesKeyboard(key, control, alt, shift, win, configuration.IgnoreExtraModifiers),
            configuration.Panic, HotkeyAction.Panic, ref best, ref modifiers);
        Consider(configuration.Left.MatchesKeyboard(key, control, alt, shift, win, configuration.IgnoreExtraModifiers),
            configuration.Left, HotkeyAction.LeftClicker, ref best, ref modifiers);
        Consider(configuration.Right.MatchesKeyboard(key, control, alt, shift, win, configuration.IgnoreExtraModifiers),
            configuration.Right, HotkeyAction.RightClicker, ref best, ref modifiers);
        return best;
    }

    private static HotkeyAction? SelectMouseAction(
        HookConfiguration configuration,
        HotkeyMouseButton button,
        bool control,
        bool alt,
        bool shift,
        bool win)
    {
        HotkeyAction? best = null;
        var modifiers = -1;
        Consider(configuration.Panic.MatchesMouse(button, control, alt, shift, win, configuration.IgnoreExtraModifiers),
            configuration.Panic, HotkeyAction.Panic, ref best, ref modifiers);
        Consider(configuration.Left.MatchesMouse(button, control, alt, shift, win, configuration.IgnoreExtraModifiers),
            configuration.Left, HotkeyAction.LeftClicker, ref best, ref modifiers);
        Consider(configuration.Right.MatchesMouse(button, control, alt, shift, win, configuration.IgnoreExtraModifiers),
            configuration.Right, HotkeyAction.RightClicker, ref best, ref modifiers);
        return best;
    }

    private static void Consider(
        bool matches,
        HotkeyBinding binding,
        HotkeyAction action,
        ref HotkeyAction? best,
        ref int modifierCount)
    {
        if (!matches || binding.ModifierCount <= modifierCount)
        {
            return;
        }

        best = action;
        modifierCount = binding.ModifierCount;
    }

    private HotkeyBinding GetBinding(HotkeyAction action) => action switch
    {
        HotkeyAction.LeftClicker => _configuration.Left,
        HotkeyAction.RightClicker => _configuration.Right,
        _ => _configuration.Panic
    };

    private static bool RequiredModifiersRemainDown(HotkeyBinding binding)
    {
        return (!binding.Control || NativeMethods.IsKeyDown(NativeMethods.VkControl))
               && (!binding.Alt || NativeMethods.IsKeyDown(NativeMethods.VkMenu))
               && (!binding.Shift || NativeMethods.IsKeyDown(NativeMethods.VkShift))
               && (!binding.Win
                   || NativeMethods.IsKeyDown(NativeMethods.VkLWin)
                   || NativeMethods.IsKeyDown(NativeMethods.VkRWin));
    }

    private static void GetModifiers(out bool control, out bool alt, out bool shift, out bool win)
    {
        control = NativeMethods.IsKeyDown(NativeMethods.VkControl);
        alt = NativeMethods.IsKeyDown(NativeMethods.VkMenu);
        shift = NativeMethods.IsKeyDown(NativeMethods.VkShift);
        win = NativeMethods.IsKeyDown(NativeMethods.VkLWin) || NativeMethods.IsKeyDown(NativeMethods.VkRWin);
    }

    private static int GetMouseVirtualKey(HotkeyMouseButton button) => button switch
    {
        HotkeyMouseButton.Middle => NativeMethods.VkMButton,
        HotkeyMouseButton.Mouse4 => NativeMethods.VkXButton1,
        HotkeyMouseButton.Mouse5 => NativeMethods.VkXButton2,
        _ => 0
    };

    private static bool TryGetMouseButton(
        int message,
        uint mouseData,
        out HotkeyMouseButton button,
        out bool isDown,
        out bool isUp)
    {
        button = HotkeyMouseButton.None;
        isDown = false;
        isUp = false;

        if (message is NativeMethods.WmMButtonDown or NativeMethods.WmMButtonUp)
        {
            button = HotkeyMouseButton.Middle;
            isDown = message == NativeMethods.WmMButtonDown;
            isUp = message == NativeMethods.WmMButtonUp;
            return true;
        }

        if (message is not (NativeMethods.WmXButtonDown or NativeMethods.WmXButtonUp))
        {
            return false;
        }

        var xButton = (mouseData >> 16) & 0xFFFF;
        button = xButton == NativeMethods.XButton1
            ? HotkeyMouseButton.Mouse4
            : xButton == NativeMethods.XButton2
                ? HotkeyMouseButton.Mouse5
                : HotkeyMouseButton.None;
        isDown = message == NativeMethods.WmXButtonDown;
        isUp = message == NativeMethods.WmXButtonUp;
        return button != HotkeyMouseButton.None;
    }

    private void RaiseHotkey(HotkeyAction action, HotkeySignal signal)
    {
        try
        {
            HotkeyChanged?.Invoke(this, new GlobalHotkeyEventArgs(action, signal));
        }
        catch
        {
            // Hook stability has priority over consumers.
        }
    }

    private void RaiseCapture(HotkeyBinding? binding, bool cancelled)
    {
        try
        {
            BindingCaptured?.Invoke(this, new HotkeyCaptureEventArgs(binding, cancelled));
        }
        catch
        {
            // Capture UI cannot break the hook chain.
        }
    }

    private IntPtr NextKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
        => NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);

    private IntPtr NextMouse(int nCode, IntPtr wParam, IntPtr lParam)
        => NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);

    private static HookConfiguration DefaultConfiguration() => new(
        HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse4),
        HotkeyBinding.FromMouse(HotkeyMouseButton.Mouse5),
        HotkeyBinding.FromKey(VirtualKeys.F12),
        true);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        if (_mouseHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
