using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus;

public class InteractionMenuItem : MenuItemBase
{
    private const string InteractionKey = "interaction";
    private const string DisableInteraction = "禁用交互";
    private const string EnableInteraction = "启用交互";

    private bool _interaction;

    public override void AddUiMenuItem(ContextMenu menu)
    {
        menu.Items.Add(new Separator());

        var item = new MenuItem { Header = _interaction ? DisableInteraction : EnableInteraction };
        item.Click += (_, _) => ToggleInteraction();
        menu.Items.Add(item);
    }

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var item = new ToolStripMenuItem(_interaction ? DisableInteraction : EnableInteraction);
        item.Click += (_, _) => ToggleInteraction();
        menu.Items.Add(item);
    }

    public override void OnMainWindowCreated(MainWindow window)
    {
        base.OnMainWindowCreated(window);
        window.Loaded += (_, _) => ApplyInteraction();
    }

    public override void OnLoadConfig(JsonObject config)
    {
        if (config[InteractionKey] is not JsonValue value || !value.TryGetValue<bool>(out var interaction)) return;
        _interaction = interaction;
        ApplyInteraction();
    }

    public override void OnSaveConfig(JsonObject config)
    {
        config[InteractionKey] = _interaction;
    }

    private void ToggleInteraction()
    {
        _interaction = !_interaction;
        ApplyInteraction();
        EventBus.Publish(EventType.ConfigChanged);
        WindowUtil.ShowToast("交互已" + (_interaction ? "启用" : "禁用"));
    }

    private void ApplyInteraction()
    {
        var hwnd = new WindowInteropHelper(Window!).Handle;
        if (hwnd != nint.Zero) Win32.SetNativeClickThrough(hwnd, _interaction);
    }
}