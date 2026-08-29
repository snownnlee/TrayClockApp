using System.Text.Json.Nodes;
using System.Windows.Forms;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace TrayClockApp.Core;

public interface IComponent
{
    void Init();

    void AddUiMenuItem(ContextMenu menu);

    void AddTrayMenuItem(ContextMenuStrip menu);

    void OnMainWindowCreated(MainWindow window);

    void OnLoadConfig(JsonObject config);

    void OnSaveConfig(JsonObject config);

    void OnStart();

    void OnStop();
}