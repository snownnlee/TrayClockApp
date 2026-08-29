using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Forms;
using TrayClockApp.Menus.MoreMenu;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus;

public class MoreMenuItem : MenuItemBase
{
    private readonly IMoreMenuItem[] _moreMenuItems =
    [
        new KeepScreenOnMoreMenuItem(),
        new KeepSystemRunMoreMenuItem()
    ];

    public override void Init()
    {
        foreach (var moreMenuItem in _moreMenuItems) moreMenuItem.Init();
    }

    public override void AddUiMenuItem(ContextMenu menu)
    {
        var moreFunctionMenu = new MenuItem { Header = "更多 >>> " };

        var hasMoreMenuItem = _moreMenuItems.Aggregate(false, (current, moreMenuItem) => current | moreMenuItem.AddUiMenuItem(moreFunctionMenu));

        if (!hasMoreMenuItem) return;
        menu.Items.Add(new Separator());
        menu.Items.Add(moreFunctionMenu);
    }

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var moreFunctionMenu = new ToolStripMenuItem("更多 >>> ");
        foreach (var moreMenuItem in _moreMenuItems) moreMenuItem.AddTrayMenuItem(moreFunctionMenu);
        menu.Items.Add(moreFunctionMenu);
    }

    public override void OnMainWindowCreated(MainWindow window)
    {
        foreach (var moreMenuItem in _moreMenuItems) moreMenuItem.OnMainWindowCreated(window);
    }

    public override void OnLoadConfig(JsonObject config)
    {
        foreach (var moreMenuItem in _moreMenuItems) moreMenuItem.OnLoadConfig(config);
    }

    public override void OnSaveConfig(JsonObject config)
    {
        foreach (var moreMenuItem in _moreMenuItems) moreMenuItem.OnSaveConfig(config);
    }

    public override void OnStart()
    {
        foreach (var moreMenuItem in _moreMenuItems) moreMenuItem.OnStart();
    }

    public override void OnStop()
    {
        foreach (var moreMenuItem in _moreMenuItems) moreMenuItem.OnStop();
    }
}