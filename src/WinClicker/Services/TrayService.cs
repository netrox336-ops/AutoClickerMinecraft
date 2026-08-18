using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace WinClicker.Services;

internal sealed class TrayService : IDisposable
{
    private sealed class DarkColorTable : Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(17, 20, 25);
        public override Color ImageMarginGradientBegin => Color.FromArgb(17, 20, 25);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(17, 20, 25);
        public override Color ImageMarginGradientEnd => Color.FromArgb(17, 20, 25);
        public override Color MenuItemSelected => Color.FromArgb(32, 37, 46);
        public override Color MenuItemBorder => Color.FromArgb(255, 59, 53);
        public override Color MenuBorder => Color.FromArgb(42, 48, 59);
        public override Color SeparatorDark => Color.FromArgb(42, 48, 59);
        public override Color SeparatorLight => Color.FromArgb(42, 48, 59);
    }

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly bool _ownsIcon;
    private bool _disposed;

    internal TrayService()
    {
        (_icon, _ownsIcon) = LoadIcon();

        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkColorTable()),
            ShowImageMargin = false,
            BackColor = Color.FromArgb(17, 20, 25),
            ForeColor = Color.FromArgb(244, 246, 248)
        };
        menu.Items.Add("Открыть Auto Clicker", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Emergency Stop", null, (_, _) => PanicRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Auto Clicker 3.0.1",
            Icon = _icon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    internal event EventHandler? OpenRequested;
    internal event EventHandler? PanicRequested;
    internal event EventHandler? ExitRequested;

    internal void ShowInfo(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3500);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_ownsIcon)
        {
            _icon.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static (Icon Icon, bool Owned) LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        if (resource is null)
        {
            return (SystemIcons.Application, false);
        }

        using (resource.Stream)
        using (var loaded = new Icon(resource.Stream))
        {
            return ((Icon)loaded.Clone(), true);
        }
    }
}
