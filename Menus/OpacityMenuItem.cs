using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus;

public class OpacityMenuItem : MenuItemBase
{
    private const string OpacityNKey = "opacityN";

    private static readonly (string Text, int Value)[] OpacityList =
    [
        ("0%", 1),
        ("10%", 10),
        ("20%", 20),
        ("30%", 30),
        ("40%", 40),
        ("50%", 50),
        ("60%", 60),
        ("70%", 70),
        ("80%", 80),
        ("90%", 90),
        ("100%", 100)
    ];

    private int _opacityN = Constants.DefaultOpacity;

    public override void AddUiMenuItem(ContextMenu menu)
    {
        menu.Items.Add(new Separator());

        var opacityMenu = new MenuItem { Header = "不透明度 >>> " };
        var selected = GetSelectedIndex();
        for (var i = 0; i < OpacityList.Length; i++)
        {
            var (text, value) = OpacityList[i];
            var item = new MenuItem
            {
                Header = text,
                IsCheckable = true,
                IsChecked = i == selected
            };
            item.Click += (_, _) =>
            {
                if (value != _opacityN) OnOpacityClicked(value, text);
            };
            opacityMenu.Items.Add(item);
        }

        menu.Items.Add(opacityMenu);
    }

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var opacityMenu = new ToolStripMenuItem("不透明度 >>> ");
        var selected = GetSelectedIndex();
        for (var i = 0; i < OpacityList.Length; i++)
        {
            var (text, value) = OpacityList[i];
            var item = new ToolStripMenuItem(text) { Checked = i == selected };
            item.Click += (_, _) => OnOpacityClicked(value, text);
            opacityMenu.DropDownItems.Add(item);
        }

        menu.Items.Add(opacityMenu);
    }

    public override void OnLoadConfig(JsonObject config)
    {
        if (config[OpacityNKey] is not JsonValue value || !value.TryGetValue<int>(out var opacityN)) return;
        if (opacityN is < 0 or > 100) return;
        _opacityN = opacityN == 0 ? 1 : opacityN;
        ChangeWindowOpacity();
    }

    public override void OnSaveConfig(JsonObject config)
    {
        config[OpacityNKey] = _opacityN;
    }

    private int GetSelectedIndex()
    {
        for (var i = 0; i < OpacityList.Length; i++)
            if (Math.Abs(OpacityList[i].Value - _opacityN) <= 5)
                return i;

        return 3;
    }

    private void OnOpacityClicked(int value, string text)
    {
        _opacityN = value;
        ChangeWindowOpacity();
        EventBus.Publish(EventType.ConfigChanged);
        WindowUtil.ShowToast("透明度已切换为：" + text);
    }

    private void ChangeWindowOpacity()
    {
        var alpha = (byte)(_opacityN * 254 / 100);
        Window!.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
    }
}