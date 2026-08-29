using System.Text.Json.Nodes;
using System.Windows.Forms;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus.MoreMenu;

public abstract class MoreMenuItemBase : IMoreMenuItem
{
    protected MainWindow? Window { get; private set; }

    public virtual void Init()
    {
    }

    public virtual bool AddUiMenuItem(MenuItem parentMenu)
    {
        return false;
    }

    public abstract void AddTrayMenuItem(ToolStripMenuItem parentMenu);

    public virtual void OnMainWindowCreated(MainWindow window)
    {
        Window = window;
    }

    public virtual void OnLoadConfig(JsonObject config)
    {
    }

    public virtual void OnSaveConfig(JsonObject config)
    {
    }

    public virtual void OnStart()
    {
    }

    public virtual void OnStop()
    {
    }
}