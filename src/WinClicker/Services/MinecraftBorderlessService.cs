using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinClicker.Services;

internal sealed record MinecraftWindowInfo(
    IntPtr Handle,
    string ProcessName,
    string WindowTitle,
    NativeMethods.NativeRect Bounds,
    NativeMethods.NativeRect MonitorBounds);

internal sealed class MinecraftBorderlessService : IDisposable
{
    private sealed record ManagedWindow(
        IntPtr Handle,
        IntPtr OriginalStyle,
        IntPtr OriginalExStyle,
        NativeMethods.WindowPlacement OriginalPlacement,
        NativeMethods.NativeRect OriginalBounds,
        bool OriginalWasFullscreen);

    private ManagedWindow? _managed;
    private bool _disposed;

    internal MinecraftWindowInfo? FindForegroundFullscreenMinecraft()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        string processName;
        try
        {
            processName = Process.GetProcessById(unchecked((int)processId)).ProcessName;
        }
        catch
        {
            return null;
        }

        var titleBuilder = new StringBuilder(512);
        NativeMethods.GetWindowText(window, titleBuilder, titleBuilder.Capacity);
        var title = titleBuilder.ToString();
        if (!IsMinecraft(processName, title))
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(window, out var bounds))
        {
            return null;
        }

        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return null;
        }

        if (!CoversMonitor(bounds, monitorInfo.rcMonitor))
        {
            return null;
        }

        return new MinecraftWindowInfo(window, processName, title, bounds, monitorInfo.rcMonitor);
    }

    internal bool IsManaged(IntPtr window) => _managed is not null && _managed.Handle == window;

    internal async Task<bool> EnableBorderlessAsync(MinecraftWindowInfo info, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows() || !NativeMethods.IsWindow(info.Handle))
        {
            return false;
        }

        if (_managed is not null && _managed.Handle != info.Handle)
        {
            await RestoreAsync(cancellationToken);
        }

        var placement = NativeMethods.WindowPlacement.Create();
        NativeMethods.GetWindowPlacement(info.Handle, ref placement);
        var originalStyle = NativeMethods.GetWindowLongPtr(info.Handle, NativeMethods.GwlStyle);
        var originalExStyle = NativeMethods.GetWindowLongPtr(info.Handle, NativeMethods.GwlExStyle);
        _managed = new ManagedWindow(
            info.Handle,
            originalStyle,
            originalExStyle,
            placement,
            info.Bounds,
            true);

        // PostMessage was not accepted by every Minecraft/driver combination in
        // 3.0.0. Because Minecraft is the verified foreground window here, a
        // marked SendInput F11 reaches the normal game input path without loading
        // code into the process. Wait until the exclusive surface actually exits.
        NativeMethods.SetForegroundWindow(info.Handle);
        await Task.Delay(120, cancellationToken);
        SendF11ToForeground();
        await WaitForWindowedTransitionAsync(info.Handle, info.MonitorBounds, cancellationToken);

        if (!NativeMethods.IsWindow(info.Handle))
        {
            _managed = null;
            return false;
        }

        NativeMethods.ShowWindow(info.Handle, NativeMethods.SwRestore);
        var style = originalStyle.ToInt64();
        style &= ~(NativeMethods.WsCaption
                   | NativeMethods.WsThickFrame
                   | NativeMethods.WsMinimizeBox
                   | NativeMethods.WsMaximizeBox
                   | NativeMethods.WsSysMenu);
        style |= NativeMethods.WsPopup;
        NativeMethods.SetWindowLongPtr(info.Handle, NativeMethods.GwlStyle, new IntPtr(style));

        var bounds = info.MonitorBounds;
        var success = NativeMethods.SetWindowPos(
            info.Handle,
            IntPtr.Zero,
            bounds.left,
            bounds.top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpFrameChanged | NativeMethods.SwpShowWindow | NativeMethods.SwpNoActivate);
        NativeMethods.DwmFlush();
        return success;
    }

    internal async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        var managed = _managed;
        _managed = null;
        if (managed is null || !OperatingSystem.IsWindows() || !NativeMethods.IsWindow(managed.Handle))
        {
            return;
        }

        NativeMethods.SetWindowLongPtr(managed.Handle, NativeMethods.GwlStyle, managed.OriginalStyle);
        NativeMethods.SetWindowLongPtr(managed.Handle, NativeMethods.GwlExStyle, managed.OriginalExStyle);
        var placement = managed.OriginalPlacement;
        NativeMethods.SetWindowPlacement(managed.Handle, ref placement);
        NativeMethods.SetWindowPos(
            managed.Handle,
            IntPtr.Zero,
            managed.OriginalBounds.left,
            managed.OriginalBounds.top,
            managed.OriginalBounds.Width,
            managed.OriginalBounds.Height,
            NativeMethods.SwpFrameChanged | NativeMethods.SwpShowWindow | NativeMethods.SwpNoActivate);

        if (managed.OriginalWasFullscreen)
        {
            await Task.Delay(220, cancellationToken);
            if (NativeMethods.GetForegroundWindow() == managed.Handle)
            {
                SendF11ToForeground();
            }
            else
            {
                // Best-effort restoration when Minecraft is no longer foreground.
                NativeMethods.PostMessage(managed.Handle, NativeMethods.WmKeyDown, new IntPtr(NativeMethods.VkF11), IntPtr.Zero);
                NativeMethods.PostMessage(managed.Handle, NativeMethods.WmKeyUp, new IntPtr(NativeMethods.VkF11), IntPtr.Zero);
            }
        }

        NativeMethods.DwmFlush();
    }

    private static bool IsMinecraft(string processName, string title)
    {
        if (processName.Equals("Minecraft.Windows", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("Minecraft", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (processName.Equals("javaw", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("java", StringComparison.OrdinalIgnoreCase))
               && title.Contains("Minecraft", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CoversMonitor(NativeMethods.NativeRect window, NativeMethods.NativeRect monitor)
    {
        const int tolerance = 3;
        return Math.Abs(window.left - monitor.left) <= tolerance
               && Math.Abs(window.top - monitor.top) <= tolerance
               && Math.Abs(window.right - monitor.right) <= tolerance
               && Math.Abs(window.bottom - monitor.bottom) <= tolerance;
    }

    private static async Task WaitForWindowedTransitionAsync(
        IntPtr window,
        NativeMethods.NativeRect monitor,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(80, cancellationToken);
            if (!NativeMethods.IsWindow(window)
                || (NativeMethods.GetWindowRect(window, out var bounds) && !CoversMonitor(bounds, monitor)))
            {
                return;
            }
        }
    }

    private static void SendF11ToForeground()
    {
        var inputs = new[]
        {
            CreateKeyboardInput(NativeMethods.VkF11, 0),
            CreateKeyboardInput(NativeMethods.VkF11, NativeMethods.KeyeventfKeyup)
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }

    private static NativeMethods.Input CreateKeyboardInput(int virtualKey, uint flags)
    {
        return new NativeMethods.Input
        {
            type = NativeMethods.InputKeyboard,
            data = new NativeMethods.InputUnion
            {
                keyboard = new NativeMethods.KeyboardInput
                {
                    wVk = unchecked((ushort)virtualKey),
                    dwFlags = flags,
                    dwExtraInfo = NativeMethods.AutoClickerInputMarker
                }
            }
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            RestoreAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort restoration on application exit.
        }

        GC.SuppressFinalize(this);
    }
}
