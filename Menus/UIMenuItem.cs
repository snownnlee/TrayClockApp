using System.Text.Json.Nodes;
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

    private const string PlainTextIconsKey = "plainTextIcons";
    private const string PlainTextIconsText = "纯文本图标模式";

    private const string BorderModeKey = "borderMode";
    private const string BorderHeaderText = "边框 >>> ";

    private static readonly (BorderMode Mode, string Text)[] BorderModes =
    [
        (BorderMode.None, "无边框"),
        (BorderMode.Normal, "普通边框"),
        (BorderMode.Colorful, "彩色边框")
    ];

    private bool _draggable = Constants.DefaultDraggable;
    private BorderMode _borderMode = Constants.DefaultBorderMode;

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
            Window!.MoveToCursorScreen();
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

        positionMenu.Items.Add(new Separator());

        var plainTextItem = new MenuItem
        {
            Header = PlainTextIconsText,
            IsCheckable = true,
            IsChecked = AppIcons.PlainText
        };
        plainTextItem.Click += (_, _) => SetPlainTextIcons(plainTextItem.IsChecked);
        positionMenu.Items.Add(plainTextItem);

        positionMenu.Items.Add(new Separator());

        var borderMenu = new MenuItem { Header = BorderHeaderText };
        foreach (var (mode, text) in BorderModes)
        {
            var item = new MenuItem { Header = text, IsCheckable = true, IsChecked = mode == _borderMode };
            item.Click += (_, _) =>
            {
                if (mode != _borderMode) SetBorderMode(mode, text);
            };
            borderMenu.Items.Add(item);
        }

        positionMenu.Items.Add(borderMenu);

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
            Window!.MoveToCursorScreen();
            EventBus.Publish(EventType.ConfigChanged);
            WindowUtil.ShowToast("位置已重置");
        };
        positionMenu.DropDownItems.Add(resetItem);

        var draggableItem = new ToolStripMenuItem(_draggable ? DisableDraggable : EnableDraggable);
        draggableItem.Click += (_, _) => ToggleDraggable();
        positionMenu.DropDownItems.Add(draggableItem);

        positionMenu.DropDownItems.Add(new ToolStripSeparator());

        var plainTextItem = new ToolStripMenuItem(PlainTextIconsText)
        {
            Checked = AppIcons.PlainText,
            CheckOnClick = true
        };
        plainTextItem.CheckedChanged += (_, _) => SetPlainTextIcons(plainTextItem.Checked);
        positionMenu.DropDownItems.Add(plainTextItem);

        positionMenu.DropDownItems.Add(new ToolStripSeparator());

        var borderMenu = new ToolStripMenuItem(BorderHeaderText);
        foreach (var (mode, text) in BorderModes)
        {
            var item = new ToolStripMenuItem(text) { Checked = mode == _borderMode };
            item.Click += (_, _) =>
            {
                if (mode != _borderMode) SetBorderMode(mode, text);
            };
            borderMenu.DropDownItems.Add(item);
        }

        positionMenu.DropDownItems.Add(borderMenu);

        menu.Items.Add(positionMenu);
    }

    public override void OnLoadConfig(JsonObject config)
    {
        if (config["X"] is JsonValue xValue && config["Y"] is JsonValue yValue &&
            xValue.TryGetValue<int>(out var x) && yValue.TryGetValue<int>(out var y))
            Window!.SetPositionFromConfig(x, y);

        if (config[PlainTextIconsKey] is JsonValue plainValue && plainValue.TryGetValue<bool>(out var plainText))
            AppIcons.SetPlainText(plainText);

        if (config[BorderModeKey] is JsonValue borderValue && borderValue.TryGetValue<string>(out var borderText)
            && Enum.TryParse(borderText, ignoreCase: true, out BorderMode borderMode))
        {
            _borderMode = borderMode;
            Window!.SetBorderMode(borderMode);
        }

        if (config[DraggableKey] is not JsonValue draggableValue || !draggableValue.TryGetValue<bool>(out var draggable)) return;
        _draggable = draggable;
        Window!.SetDraggable(_draggable);
    }

    public override void OnSaveConfig(JsonObject config)
    {
        if (!double.IsNaN(Window!.Left)) config["X"] = (int)Window.Left;
        if (!double.IsNaN(Window!.Top)) config["Y"] = (int)Window.Top;

        config[DraggableKey] = _draggable;
        config[PlainTextIconsKey] = AppIcons.PlainText;
        config[BorderModeKey] = _borderMode.ToString().ToLowerInvariant();
    }

    private void SetPlainTextIcons(bool plainText)
    {
        if (AppIcons.PlainText == plainText) return;

        AppIcons.SetPlainText(plainText);
        EventBus.Publish(EventType.ConfigChanged);
        WindowUtil.ShowToast(plainText ? "已启用纯文本图标模式" : "已启用 emoji 图标模式");
    }

    private void SetBorderMode(BorderMode mode, string text)
    {
        _borderMode = mode;
        Window!.SetBorderMode(mode);
        EventBus.Publish(EventType.ConfigChanged);
        WindowUtil.ShowToast("边框已切换为：" + text);
    }

    private void ToggleDraggable()
    {
        _draggable = !_draggable;
        Window!.SetDraggable(_draggable);
        EventBus.Publish(EventType.ConfigChanged);
        WindowUtil.ShowToast("拖拽已" + (_draggable ? "启用" : "禁用"));
    }
}