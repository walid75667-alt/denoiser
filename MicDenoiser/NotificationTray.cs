using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MicDenoiser;

/// <summary>Owns the native tray icon and menu on the WPF UI thread.</summary>
internal sealed class NotificationTray : IDisposable
{
    private readonly Drawing.Icon _icon;
    private readonly Forms.NotifyIcon _notification;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _restore, _toggle, _exit;

    public NotificationTray(Action restore, Action toggle, Action exit)
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/MicDenoiser.ico"))
            ?? throw new InvalidOperationException("The embedded application icon is missing.");
        using (resource.Stream)
        using (var source = new Drawing.Icon(resource.Stream, Forms.SystemInformation.SmallIconSize))
            _icon = (Drawing.Icon)source.Clone();
        _menu = new Forms.ContextMenuStrip();
        _restore = new Forms.ToolStripMenuItem();
        _toggle = new Forms.ToolStripMenuItem();
        _exit = new Forms.ToolStripMenuItem();
        _restore.Click += (_, _) => restore();
        _toggle.Click += (_, _) => toggle();
        _exit.Click += (_, _) => exit();
        _menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            _restore, _toggle, new Forms.ToolStripSeparator(), _exit
        });
        _notification = new Forms.NotifyIcon { Icon = _icon, ContextMenuStrip = _menu };
        _notification.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) restore(); };
        _notification.BalloonTipClicked += (_, _) => restore();
        Refresh(false, false);
        _notification.Visible = true;
    }

    public void Refresh(bool running, bool starting)
    {
        _menu.RightToLeft = UiStrings.Language == "ar" ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;
        _restore.Text = UiStrings.Get("TrayOpen");
        _toggle.Text = UiStrings.Get(running ? "TrayStop" : "Ui019");
        _toggle.Enabled = !starting;
        _exit.Text = UiStrings.Get("TrayExit");
        _notification.Text = UiStrings.Get(starting ? "TrayPreparing" : running ? "TrayRunning" : "TrayReady");
    }

    public void SetDarkTheme(bool dark)
    {
        _menu.RenderMode = Forms.ToolStripRenderMode.System;
        _menu.BackColor = dark ? Drawing.Color.FromArgb(27, 41, 54) : Drawing.SystemColors.Control;
        _menu.ForeColor = dark ? Drawing.Color.FromArgb(230, 238, 246) : Drawing.SystemColors.ControlText;
    }

    public void ShowNotice(string title, string text, bool error = false) =>
        _notification.ShowBalloonTip(4000, title, text.Length > 240 ? text[..240] + "…" : text,
            error ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _notification.Visible = false;
        _notification.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
