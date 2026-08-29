using System.Text.Json.Nodes;
using System.Windows.Forms;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace TrayClockApp.Menus;

public abstract class MenuItemBase : IMenuItem
{
    protected MainWindow? Window { get; private set; }

    public virtual void Init()
    {
    }

    public virtual void AddUiMenuItem(ContextMenu menu)
    {
    }

    public virtual void AddTrayMenuItem(ContextMenuStrip menu)
    {
    }

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