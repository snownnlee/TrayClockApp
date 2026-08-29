using System.Windows.Forms;
using TrayClockApp.Menus;

namespace TrayClockApp.Infra;

public static class TrayIconManager
{
    public static void Create(NotifyIcon icon, List<IMenuItem> menuItems, MainWindow window)
    {
        icon.Icon = AppIcon.DrawTrayIcon();
        icon.Text = "Tray Clock";
        icon.ContextMenuStrip = new ContextMenuStrip();

        icon.ContextMenuStrip.Opening += (_, _) =>
        {
            icon.ContextMenuStrip.Items.Clear();
            BuildMenu(icon.ContextMenuStrip, menuItems);
        };
        BuildMenu(icon.ContextMenuStrip, menuItems);
        icon.Visible = true;

        icon.MouseClick += async (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;

            await Task.Delay(TimeSpan.FromSeconds(2));
            WindowUtil.BringToFront(window);
        };
    }

    private static void BuildMenu(ContextMenuStrip menu, List<IMenuItem> menuItems)
    {
        foreach (var menuItem in menuItems) menuItem.AddTrayMenuItem(menu);
    }

    public static void Dispose(NotifyIcon icon)
    {
        icon.Visible = false;
        icon.Dispose();
    }
}