using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus;

public class UiMenuItem : MenuItemBase
{
    private const string DraggableKey = "draggable";
    private const string DisableDraggable = "禁用拖拽";
    private const string EnableDraggable = "启用拖拽";

    private bool _draggable = Constants.DefaultDraggable;

    public override void AddUiMenuItem(ContextMenu menu)
    {
        menu.Items.Add(new Separator());

        var positionMenu = new MenuItem { Header = "界面 >>> " };

        var refreshItem = new MenuItem { Header = "刷新" };
        refreshItem.Click += (_, _) =>
        {
            Window!.UpdateLayout();
            WindowUtil.ShowToast("界面已刷新");
        };
        positionMenu.Items.Add(refreshItem);

        var resetItem = new MenuItem { Header = "重置位置" };
        resetItem.Click += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Window!.Left = (workArea.Width - Window.ActualWidth) / 3;
            Window!.Top = 0;
            EventBus.Publish(EventType.ConfigChanged);
            WindowUtil.ShowToast("位置已重置");
        };
        positionMenu.Items.Add(resetItem);

        var draggableItem = new MenuItem { Header = _draggable ? DisableDraggable : EnableDraggable };
        draggableItem.Click += (_, _) =>
        {
            ToggleDraggable();
            draggableItem.Header = _draggable ? DisableDraggable : EnableDraggable;
        };
        positionMenu.Items.Add(draggableItem);

        menu.Items.Add(positionMenu);
    }

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var positionMenu = new ToolStripMenuItem("界面 >>> ");

        var refreshItem = new ToolStripMenuItem("刷新");
        refreshItem.Click += (_, _) =>
        {
            Window!.UpdateLayout();
            WindowUtil.ShowToast("界面已刷新");
        };
        positionMenu.DropDownItems.Add(refreshItem);

        var resetItem = new ToolStripMenuItem("重置位置");
        resetItem.Click += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Window!.Left = (workArea.Width - Window.ActualWidth) / 3;
            Window!.Top = 0;
            EventBus.Publish(EventType.ConfigChanged);
            WindowUtil.ShowToast("位置已重置");
        };
        positionMenu.DropDownItems.Add(resetItem);

        var draggableItem = new ToolStripMenuItem(_draggable ? DisableDraggable : EnableDraggable);
        draggableItem.Click += (_, _) => ToggleDraggable();
        positionMenu.DropDownItems.Add(draggableItem);

        menu.Items.Add(positionMenu);
    }

    public override void OnLoadConfig(JsonObject config)
    {
        if (config["X"] is JsonValue xValue && config["Y"] is JsonValue yValue &&
            xValue.TryGetValue<int>(out var x) && yValue.TryGetValue<int>(out var y))
            Window!.SetPositionFromConfig(x, y);

        if (config[DraggableKey] is not JsonValue draggableValue || !draggableValue.TryGetValue<bool>(out var draggable)) return;
        _draggable = draggable;
        Window!.SetDraggable(_draggable);
    }

    public override void OnSaveConfig(JsonObject config)
    {
        if (!double.IsNaN(Window!.Left)) config["X"] = (int)Window.Left;
        if (!double.IsNaN(Window!.Top)) config["Y"] = (int)Window.Top;

        config[DraggableKey] = _draggable;
    }

    private void ToggleDraggable()
    {
        _draggable = !_draggable;
        Window!.SetDraggable(_draggable);
        EventBus.Publish(EventType.ConfigChanged);
        WindowUtil.ShowToast("拖拽已" + (_draggable ? "启用" : "禁用"));
    }
}