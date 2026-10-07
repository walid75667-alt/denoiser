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
    private readonly Forms.ToolStripMenuItem _restore, _toggle, _exit, _mute, _levels;
    private readonly Dictionary<string, Forms.ToolStripMenuItem> _levelItems = new();

    public NotificationTray(Action restore, Action toggle, Action exit, Action mute, Action<string> level, Action settings)
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
        _mute = new Forms.ToolStripMenuItem(); _mute.Click += (_, _) => mute();
        _levels = new Forms.ToolStripMenuItem();
        foreach (var key in new[] { "light", "balanced", "strong" })
        {
            var item = new Forms.ToolStripMenuItem(); item.Click += (_, _) => level(key);
            _levels.DropDownItems.Add(item); _levelItems.Add(key, item);
        }
        var preferences = new Forms.ToolStripMenuItem(); preferences.Click += (_, _) => settings();
        preferences.Name = "preferences";
        _menu.Items.Add(preferences);
        _restore.Click += (_, _) => restore();
        _toggle.Click += (_, _) => toggle();
        _exit.Click += (_, _) => exit();
        _menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            _restore, _toggle, _mute, _levels, new Forms.ToolStripSeparator(), _exit
        });
        _notification = new Forms.NotifyIcon { Icon = _icon, ContextMenuStrip = _menu };
        _notification.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) restore(); };
        _notification.BalloonTipClicked += (_, _) => restore();
        Refresh(false, false);
        _notification.Visible = true;
    }

    public void Refresh(bool running, bool starting, bool muted = false, string level = "balanced")
    {
        _menu.RightToLeft = UiStrings.Language == "ar" ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;
        _restore.Text = UiStrings.Get("TrayOpen");
        _menu.Items["preferences"]!.Text = UiStrings.Get("PreferencesTitle");
        _mute.Text = UiStrings.Get(muted ? "Unmute" : "Mute") + " (Ctrl+Alt+M)";
        _mute.Enabled = _levels.Enabled = !starting; _mute.Checked = muted;
        _levels.Text = UiStrings.Get("IsolationLevel");
        foreach (var entry in _levelItems) { entry.Value.Text = UiStrings.Get("Level" + char.ToUpperInvariant(entry.Key[0]) + entry.Key[1..]); entry.Value.Checked = entry.Key == level; }
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
