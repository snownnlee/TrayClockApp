using System.Text.Json.Nodes;
using System.Windows.Forms;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus.MoreMenu;

public interface IMoreMenuItem
{
    void Init();

    bool AddUiMenuItem(MenuItem parentMenu);

    void AddTrayMenuItem(ToolStripMenuItem parentMenu);

    void OnMainWindowCreated(MainWindow window);

    void OnLoadConfig(JsonObject config);

    void OnSaveConfig(JsonObject config);

    void OnStart();

    void OnStop();
}