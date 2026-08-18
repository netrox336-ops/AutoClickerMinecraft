using System.Diagnostics;
using System.Runtime.InteropServices;
using WinClicker.Models;

namespace WinClicker.Services;

internal sealed class ClickChannelEventArgs : EventArgs
{
    internal ClickChannelEventArgs(ClickButton button, bool isRunning)
    {
        Button = button;
        IsRunning = isRunning;
    }

    internal ClickButton Button { get; }
    internal bool IsRunning { get; }
}

internal interface IInputInjector
{
    bool Click(ClickButton button);
    void Release(ClickButton button);
    void ReleaseAll();
}

internal sealed class SendInputInjector : IInputInjector
{
    private readonly NativeMethods.Input[] _leftClick = BuildClickInputs(ClickButton.Left);
    private readonly NativeMethods.Input[] _rightClick = BuildClickInputs(ClickButton.Right);
    private readonly NativeMethods.Input[] _leftRelease = [CreateMouseInput(NativeMethods.MouseeventfLeftup)];
    private readonly NativeMethods.Input[] _rightRelease = [CreateMouseInput(NativeMethods.MouseeventfRightup)];

    public bool Click(ClickButton button)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var inputs = button == ClickButton.Left ? _leftClick : _rightClick;
        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) == inputs.Length;
    }

    public void Release(ClickButton button)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var inputs = button == ClickButton.Left ? _leftRelease : _rightRelease;
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }

    public void ReleaseAll()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var inputs = new[]
        {
            CreateMouseInput(NativeMethods.MouseeventfLeftup),
            CreateMouseInput(NativeMethods.MouseeventfRightup)
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }

    private static NativeMethods.Input[] BuildClickInputs(ClickButton button)
    {
        var down = button == ClickButton.Left
            ? NativeMethods.MouseeventfLeftdown
            : NativeMethods.MouseeventfRightdown;
        var up = button == ClickButton.Left
            ? NativeMethods.MouseeventfLeftup
            : NativeMethods.MouseeventfRightup;
        return [CreateMouseInput(down), CreateMouseInput(up)];
    }

    private static NativeMethods.Input CreateMouseInput(uint flags)
    {
        return new NativeMethods.Input
        {
            type = NativeMethods.InputMouse,
            data = new NativeMethods.InputUnion
            {
                mouse = new NativeMethods.MouseInput
                {
                    dwFlags = flags,
                    dwExtraInfo = NativeMethods.AutoClickerInputMarker
                }
            }
        };
    }
}

internal sealed class ClickEngine : IDisposable
{
    private sealed class Channel
    {
        internal Channel(ClickButton button, int interval)
        {
            Button = button;
            IntervalMs = interval;
        }

        internal readonly object Gate = new();
        internal readonly ClickButton Button;
        internal Thread? Worker;
        internal ManualResetEvent? StopSignal;
        internal int Generation;
        internal int IntervalMs;
        internal long DeliveredClicks;
        internal bool Running;
    }

    private readonly IInputInjector _injector;
    private readonly Channel _left = new(ClickButton.Left, 50);
    private readonly Channel _right = new(ClickButton.Right, 50);
    private bool _disposed;

    internal ClickEngine()
        : this(new SendInputInjector())
    {
    }

    internal ClickEngine(IInputInjector injector)
    {
        _injector = injector;
    }

    internal event EventHandler<ClickChannelEventArgs>? ChannelStateChanged;

    internal int LeftIntervalMs
    {
        get => Volatile.Read(ref _left.IntervalMs);
        set => Volatile.Write(ref _left.IntervalMs, Math.Clamp(value, 1, 1000));
    }

    internal int RightIntervalMs
    {
        get => Volatile.Read(ref _right.IntervalMs);
        set => Volatile.Write(ref _right.IntervalMs, Math.Clamp(value, 1, 1000));
    }

    internal bool IsRunning(ClickButton button) => Volatile.Read(ref GetChannel(button).Running);

    internal long GetDeliveredClicks(ClickButton button)
        => Interlocked.Read(ref GetChannel(button).DeliveredClicks);

    internal bool Start(ClickButton button)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var channel = GetChannel(button);

        lock (channel.Gate)
        {
            if (channel.Worker is { IsAlive: true })
            {
                return false;
            }

            channel.StopSignal?.Dispose();
            channel.StopSignal = new ManualResetEvent(false);
            var signal = channel.StopSignal;
            var generation = unchecked(++channel.Generation);
            channel.Running = true;
            channel.Worker = new Thread(() => WorkerLoop(channel, signal, generation))
            {
                IsBackground = true,
                Name = button == ClickButton.Left ? "AutoClicker.LMB" : "AutoClicker.RMB",
                // Normal priority is deliberate: the clicker must never starve the
                // render/input threads of a foreground game.
                Priority = ThreadPriority.Normal
            };
            channel.Worker.Start();
        }

        RaiseStateChanged(button, true);
        return true;
    }

    internal void Stop(ClickButton button)
    {
        var channel = GetChannel(button);
        StopChannel(channel, 750);
        _injector.Release(button);
    }

    internal void Toggle(ClickButton button)
    {
        if (IsRunning(button))
        {
            Stop(button);
        }
        else
        {
            Start(button);
        }
    }

    internal void TriggerOnce(ClickButton button)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_injector.Click(button))
        {
            Interlocked.Increment(ref GetChannel(button).DeliveredClicks);
        }

        _injector.Release(button);
    }

    internal void PanicStop()
    {
        SignalChannel(_left);
        SignalChannel(_right);
        _injector.ReleaseAll();
        JoinChannel(_left, 250);
        JoinChannel(_right, 250);
        _injector.ReleaseAll();
    }

    internal void ResetStatistics()
    {
        Interlocked.Exchange(ref _left.DeliveredClicks, 0);
        Interlocked.Exchange(ref _right.DeliveredClicks, 0);
    }

    private void WorkerLoop(Channel channel, ManualResetEvent stopSignal, int generation)
    {
        NativeMethods.timeBeginPeriod(1);
        var deadline = Stopwatch.GetTimestamp();
        try
        {
            while (!stopSignal.WaitOne(0) && generation == Volatile.Read(ref channel.Generation))
            {
                if (_injector.Click(channel.Button))
                {
                    Interlocked.Increment(ref channel.DeliveredClicks);
                }

                var interval = Math.Clamp(Volatile.Read(ref channel.IntervalMs), 1, 1000);
                var intervalTicks = Math.Max(1L, Stopwatch.Frequency * interval / 1000);
                deadline += intervalTicks;

                var now = Stopwatch.GetTimestamp();
                if (deadline < now - intervalTicks)
                {
                    deadline = now + intervalTicks;
                }

                if (WaitUntil(stopSignal, deadline))
                {
                    break;
                }
            }
        }
        finally
        {
            _injector.Release(channel.Button);
            NativeMethods.timeEndPeriod(1);

            var notify = false;
            lock (channel.Gate)
            {
                if (ReferenceEquals(channel.StopSignal, stopSignal))
                {
                    channel.Worker = null;
                    channel.Running = false;
                    notify = true;
                }
            }

            if (notify)
            {
                RaiseStateChanged(channel.Button, false);
            }
        }
    }

    private static bool WaitUntil(WaitHandle stopSignal, long deadline)
    {
        while (true)
        {
            var remainingTicks = deadline - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
            {
                return false;
            }

            var remainingMilliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;
            // 3.0.0 busy-spun during the final ~1.3 ms of every interval. At small
            // intervals that could consume a full CPU core and make Minecraft
            // stutter while Hold was active. The multimedia timer already gives
            // WaitOne millisecond granularity, so a blocking wait is both stable
            // and substantially cheaper.
            if (stopSignal.WaitOne(Math.Max(1, (int)Math.Ceiling(remainingMilliseconds))))
            {
                return true;
            }
        }
    }

    private void StopChannel(Channel channel, int joinMilliseconds)
    {
        SignalChannel(channel);
        JoinChannel(channel, joinMilliseconds);
    }

    private void SignalChannel(Channel channel)
    {
        var notify = false;
        lock (channel.Gate)
        {
            unchecked
            {
                channel.Generation++;
            }

            channel.StopSignal?.Set();
            if (channel.Worker is null && channel.Running)
            {
                channel.Running = false;
                notify = true;
            }
        }

        if (notify)
        {
            RaiseStateChanged(channel.Button, false);
        }
    }

    private static void JoinChannel(Channel channel, int milliseconds)
    {
        Thread? worker;
        lock (channel.Gate)
        {
            worker = channel.Worker;
        }

        if (worker is { IsAlive: true } && worker != Thread.CurrentThread)
        {
            worker.Join(milliseconds);
        }
    }

    private void RaiseStateChanged(ClickButton button, bool running)
    {
        try
        {
            ChannelStateChanged?.Invoke(this, new ClickChannelEventArgs(button, running));
        }
        catch
        {
            // UI errors must never compromise worker cleanup.
        }
    }

    private Channel GetChannel(ClickButton button) => button == ClickButton.Left ? _left : _right;

    internal static void EmergencyReleaseAll()
    {
        try
        {
            new SendInputInjector().ReleaseAll();
        }
        catch
        {
            // Best effort on a fatal application path.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PanicStop();
        _left.StopSignal?.Dispose();
        _right.StopSignal?.Dispose();
        GC.SuppressFinalize(this);
    }
}
