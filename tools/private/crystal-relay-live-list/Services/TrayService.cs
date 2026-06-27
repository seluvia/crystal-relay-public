using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace CrystalRelayLiveList.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private bool disposed;

    public TrayService(Window window, Action onShow, Action onRefresh)
    {
        notifyIcon = new NotifyIcon
        {
            Text = "Crystal Relay Live Feedback",
            Visible = true,
            Icon = LoadEmbeddedIcon()
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => onShow());
        menu.Items.Add("Refresh", null, (_, _) => onRefresh());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) =>
        {
            notifyIcon.Visible = false;
            Application.Current.Shutdown();
        });
        notifyIcon.ContextMenuStrip = menu;
        notifyIcon.DoubleClick += (_, _) => onShow();

        window.Closing += (_, _) => notifyIcon.Visible = false;
    }

    public void ShowBalloon(string title, string message)
    {
        if (disposed) return;
        notifyIcon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
    }

    private static Icon LoadEmbeddedIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "crystal-relay-icon.ico");
            if (File.Exists(path))
            {
                return new Icon(path);
            }
        }
        catch
        {
            // fall through to default
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
