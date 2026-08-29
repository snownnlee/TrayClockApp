using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using WF = System.Windows.Forms;

namespace TrayClockApp.Labels;

public class BatteryLabel : LabelBase
{
    private const string SelectedPowerSource = "selectedPowerSource";

    /// <summary>Emoji：禁止进入标志 🚫，用于表示"没有检测到电池"。</summary>
    private const string EmojiNo = "\U0001F6AB";

    /// <summary>Emoji：电池 🔋，电量图标前缀。</summary>
    private const string EmojiBattery = "\U0001F50B";

    /// <summary>Emoji：电源插头 🔌，表示当前正在使用交流电源（插电）。</summary>
    private const string EmojiPowerOn = "\U0001F50C";

    /// <summary>Emoji：闪电 ⚡，表示电池正在充电。</summary>
    private const string EmojiCharging = "\u26A1";

    /// <summary>初始占位文本：电池图标 + "--.--%"（等待第一次采样结果）。</summary>
    private const string InitBattery = EmojiBattery + " --.--%";

    private volatile string _cachedBattery = InitBattery;

    private volatile string? _currentPowerSourceName;

    private volatile string? _currentPowerSourcePath;

    private volatile bool _hasBattery;

    private volatile List<BatteryInfo> _powerSources = [];

    private readonly List<BatteryInfo> _bufferA = [];
    private readonly List<BatteryInfo> _bufferB = [];
    private bool _bufferToggle;

    private Timer? _sampler;

    public override void Init()
    {
        base.Init();
        Text.Width = 150;

        _powerSources = BatteryIo.QueryAll();
        _hasBattery = _powerSources.Count > 0;

        if (_hasBattery)
        {
            Text.Text = InitBattery;
            Text.Foreground = Brushes.Lime;

            var powerSource = _powerSources.First();
            _currentPowerSourceName = powerSource.DeviceName;
            _currentPowerSourcePath = powerSource.DevicePath;
        }
        else
        {
            Text.Text = EmojiBattery + EmojiNo;
            Text.Foreground = Brushes.Gray;
        }
    }

    public override void Update()
    {
        Text.Text = _hasBattery ? _cachedBattery : EmojiBattery + EmojiNo;
        Text.Foreground = _hasBattery ? Brushes.Lime : Brushes.Gray;
    }

    public override void OnLoadConfig(JsonObject config)
    {
        var node = config[SelectedPowerSource];
        if (node is null) return;

        var configName = node.GetValue<string>();
        foreach (var ps in _powerSources.Where(ps => ps.DeviceName == configName))
        {
            _currentPowerSourceName = ps.DeviceName;
            _currentPowerSourcePath = ps.DevicePath;
            return;
        }

        WindowUtil.ShowToast("未找到读取的配置电源：" + configName);
    }

    public override void OnSaveConfig(JsonObject config)
    {
        if (!string.IsNullOrEmpty(_currentPowerSourceName)) config[SelectedPowerSource] = _currentPowerSourceName;
    }

    public override void AddUiMenuItem(ContextMenu menu)
    {
        menu.Items.Add(new Separator());
        var batteryMenu = new MenuItem { Header = "电源 >>> " };
        var powerSources = _powerSources;
        if (powerSources.Count == 0)
            batteryMenu.Items.Add(new MenuItem { Header = "未检测到电源", IsEnabled = false });
        else
            foreach (var ps in powerSources)
            {
                var isCurrent = ps.DeviceName == _currentPowerSourceName;
                var item = new MenuItem { Header = ps.DeviceName };
                if (isCurrent)
                    item.IsChecked = true;
                BuildDropDown(item, ps, () => menu.IsOpen = false);
                batteryMenu.Items.Add(item);
            }

        menu.Items.Add(batteryMenu);
    }

    public override void AddTrayMenuItem(WF.ContextMenuStrip menu)
    {
        menu.Items.Add(new WF.ToolStripSeparator());
        var batteryMenu = new WF.ToolStripMenuItem("电源 >>> ");
        var powerSources = _powerSources;
        if (powerSources.Count == 0)
            batteryMenu.DropDownItems.Add(new WF.ToolStripMenuItem("未检测到电源") { Enabled = false });
        else
            foreach (var ps in powerSources)
            {
                var isCurrent = ps.DeviceName == _currentPowerSourceName;
                var item = new WF.ToolStripMenuItem(ps.DeviceName) { Checked = isCurrent };
                BuildDropDown(item, ps, () => CloseWholeMenu(item));
                batteryMenu.DropDownItems.Add(item);
            }

        menu.Items.Add(batteryMenu);
    }

    private void BuildDropDown(MenuItem item, BatteryInfo ps, Action closeMenu)
    {
        item.Items.Add(InfoItemWpf("设备名称：" + ps.DeviceName));
        item.Items.Add(InfoItemWpf("序列编号：" + ps.SerialNumber));
        item.Items.Add(InfoItemWpf("当前容量：" + (ps.CurrentCapacity > 0 ? ps.CurrentCapacity + " " + ps.CapacityUnits : "未知")));
        item.Items.Add(InfoItemWpf("最大容量：" + (ps.FullChargeCapacity > 0 ? ps.FullChargeCapacity + " " + ps.CapacityUnits : "未知")));
        item.Items.Add(InfoItemWpf("设计容量：" + (ps.DesignCapacity > 0 ? ps.DesignCapacity + " " + ps.CapacityUnits : "未知")));

        item.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FindAncestorMenuItem(e.OriginalSource as DependencyObject) != item) return;

            SwitchPowerSource(ps);
            closeMenu();
            e.Handled = true;
        };
    }

    private static MenuItem? FindAncestorMenuItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is MenuItem mi) return mi;
            source = VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private void BuildDropDown(WF.ToolStripMenuItem item, BatteryInfo ps, Action closeMenu)
    {
        item.DropDownItems.Add(InfoItemTray("设备名称：" + ps.DeviceName));
        item.DropDownItems.Add(InfoItemTray("序列编号：" + ps.SerialNumber));
        item.DropDownItems.Add(InfoItemTray("当前容量：" + (ps.CurrentCapacity > 0 ? ps.CurrentCapacity + " " + ps.CapacityUnits : "未知")));
        item.DropDownItems.Add(InfoItemTray("最大容量：" + (ps.FullChargeCapacity > 0 ? ps.FullChargeCapacity + " " + ps.CapacityUnits : "未知")));
        item.DropDownItems.Add(InfoItemTray("设计容量：" + (ps.DesignCapacity > 0 ? ps.DesignCapacity + " " + ps.CapacityUnits : "未知")));

        item.MouseDown += (_, _) =>
        {
            SwitchPowerSource(ps);
            closeMenu();
        };
    }

    private void SwitchPowerSource(BatteryInfo batteryInfo)
    {
        if (batteryInfo.DeviceName == _currentPowerSourceName) return;
        _currentPowerSourceName = batteryInfo.DeviceName;
        _currentPowerSourcePath = batteryInfo.DevicePath;

        _cachedBattery = InitBattery;

        EventBus.Publish(EventType.ConfigChanged);
        WindowUtil.ShowToast("电源已切换为：" + _currentPowerSourceName);
    }

    private static WF.ToolStripMenuItem InfoItemTray(string text)
    {
        var item = new WF.ToolStripMenuItem(text);
        item.Click += (_, _) => CopyInfo(text);
        return item;
    }

    private static MenuItem InfoItemWpf(string text)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => CopyInfo(text);
        return item;
    }

    private static void CopyInfo(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"复制失败: {ex.Message}");
        }

        WindowUtil.ShowToast("已复制：" + text);
    }

    private static void CloseWholeMenu(WF.ToolStripDropDownItem item)
    {
        var current = item;
        var visited = new HashSet<WF.ToolStripDropDownItem> { current };
        while (current.OwnerItem is WF.ToolStripDropDownItem parent && visited.Add(parent))
            current = parent;

        (current.GetCurrentParent() as WF.ToolStripDropDown)?.Close();
    }

    protected override void StartOnce()
    {
        _sampler = new Timer(_ => CalculateBattery(), null, 0, 1000);
    }

    public override void OnStop()
    {
        _sampler?.Dispose();
        _sampler = null;
        base.OnStop();
    }

    private void CalculateBattery()
    {
        var buffer = _bufferToggle ? _bufferB : _bufferA;
        _bufferToggle = !_bufferToggle;

        BatteryIo.RefreshInto(buffer, refresh: true);
        _hasBattery = buffer.Count > 0;
        _powerSources = buffer;
        if (!_hasBattery)
        {
            _cachedBattery = EmojiBattery + EmojiNo;
            return;
        }

        var battery = buffer.FirstOrDefault(ps => ps.DevicePath == _currentPowerSourcePath);
        if (battery is null)
        {
            battery = buffer.First();
            if (battery.FullChargeCapacity <= 0)
            {
                _cachedBattery = InitBattery;
                return;
            }

            SwitchPowerSource(battery);
        }

        var percent = Math.Clamp(battery.CurrentCapacity * 100.0 / battery.FullChargeCapacity, 0, 100);

        _cachedBattery = EmojiBattery + $"{percent:F2}%" +
                         (battery.PowerOnLine ? EmojiPowerOn : "") +
                         (battery.Charging ? EmojiCharging : "");
    }
}