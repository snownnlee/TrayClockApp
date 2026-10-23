using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Forms;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus;

/// <summary>防烧屏顶级菜单：组合缓变亮度与缓变配色两个独立子菜单。</summary>
public class AntiBurnInMenuItem : MenuItemBase
{
    private const string HeaderText = "防烧屏 >>> ";

    private readonly BrightnessMenuItem _brightness = new();
    private readonly ColorMenuItem _color = new();

    public override void Init()
    {
        _brightness.Init();
        _color.Init();
    }

    public override void OnMainWindowCreated(MainWindow window)
    {
        base.OnMainWindowCreated(window);
        _brightness.OnMainWindowCreated(window);
        _color.OnMainWindowCreated(window);
    }

    public override void AddUiMenuItem(ContextMenu menu)
    {
        menu.Items.Add(new Separator());

        var antiBurnInMenu = new MenuItem { Header = HeaderText };
        _brightness.AddUiSubMenu(antiBurnInMenu);
        antiBurnInMenu.Items.Add(new Separator());
        _color.AddUiSubMenu(antiBurnInMenu);

        menu.Items.Add(antiBurnInMenu);
    }

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var antiBurnInMenu = new ToolStripMenuItem(HeaderText);
        _brightness.AddTraySubMenu(antiBurnInMenu);
        antiBurnInMenu.DropDownItems.Add(new ToolStripSeparator());
        _color.AddTraySubMenu(antiBurnInMenu);

        menu.Items.Add(antiBurnInMenu);
    }

    public override void OnLoadConfig(JsonObject config)
    {
        _brightness.OnLoadConfig(config);
        _color.OnLoadConfig(config);
    }

    public override void OnSaveConfig(JsonObject config)
    {
        _brightness.OnSaveConfig(config);
        _color.OnSaveConfig(config);
    }

    public override void OnStart()
    {
        _brightness.OnStart();
        _color.OnStart();
    }

    public override void OnStop()
    {
        _brightness.OnStop();
        _color.OnStop();
    }
}
