using System.Text.Json.Nodes;
using System.Windows.Forms;
using System.Windows.Threading;
using TrayClockApp.Core;
using TrayClockApp.Infra;

namespace TrayClockApp.Menus;

public class ToFrontMenuItem : MenuItemBase
{
    private const string AutoToFrontKey = "autoToFront";

    private const string AutoToFrontText = "自动检测置顶";

    private static readonly TimeSpan AutoToFrontInterval = TimeSpan.FromSeconds(3);

    private static readonly (string Text, int DelayMs)[] DelayList =
    [
        ("立即", 0),
        ("1秒后", 1000),
        ("3秒后", 3000),
        ("5秒后", 5000),
        ("7秒后", 7000),
        ("9秒后", 9000)
    ];

    private bool _autoToFrontOn;
    private DispatcherTimer? _autoToFrontTimer;

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var item = new ToolStripMenuItem("置顶");
        item.MouseUp += async (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            menu.Close();
            await BringToFrontAsync(0);
        };

        var autoToFrontItem = new ToolStripMenuItem(AutoToFrontText)
        {
            Checked = _autoToFrontOn,
            CheckOnClick = true
        };
        autoToFrontItem.CheckedChanged += (_, _) => SetAutoToFront(autoToFrontItem.Checked);
        item.DropDownItems.Add(autoToFrontItem);

        item.DropDownItems.Add(new ToolStripSeparator());

        foreach (var (text, delayMs) in DelayList)
        {
            var subItem = new ToolStripMenuItem(text);
            subItem.Click += async (_, _) => await BringToFrontAsync(delayMs);
            item.DropDownItems.Add(subItem);
        }

        menu.Items.Add(item);
    }

    public override void OnLoadConfig(JsonObject config)
    {
        if (config[AutoToFrontKey] is not JsonValue value || !value.TryGetValue<bool>(out var on)) return;
        _autoToFrontOn = on;
        ApplyAutoToFront();
    }

    public override void OnSaveConfig(JsonObject config)
    {
        config[AutoToFrontKey] = _autoToFrontOn;
    }

    private void SetAutoToFront(bool on)
    {
        if (_autoToFrontOn == on) return;

        _autoToFrontOn = on;
        ApplyAutoToFront();
        EventBus.Publish(EventType.ConfigChanged);
        WindowUtil.ShowToast(on ? "自动检测置顶已开启" : "自动检测置顶已关闭");
    }

    private void ApplyAutoToFront()
    {
        if (!_autoToFrontOn)
        {
            _autoToFrontTimer?.Stop();
            return;
        }

        if (_autoToFrontTimer is null)
        {
            _autoToFrontTimer = new DispatcherTimer { Interval = AutoToFrontInterval };
            _autoToFrontTimer.Tick += (_, _) => WindowUtil.KeepOnTop(Window);
        }

        _autoToFrontTimer.Start();
    }

    public override void OnStop()
    {
        _autoToFrontOn = false;
        _autoToFrontTimer?.Stop();
        _autoToFrontTimer = null;
        WindowUtil.ResetTopmostCache();
        base.OnStop();
    }

    private async Task BringToFrontAsync(int delayMs)
    {
        if (delayMs > 0)
        {
            WindowUtil.ShowToast($"{delayMs / 1000.0:0.#}秒后执行置顶", TimeSpan.FromMilliseconds(delayMs), true);
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
        }

        WindowUtil.BringToFront(Window);
        WindowUtil.ShowToast("已执行置顶");
    }
}