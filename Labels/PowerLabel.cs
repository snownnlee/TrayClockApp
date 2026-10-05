using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using WF = System.Windows.Forms;

namespace TrayClockApp.Labels;

public class PowerLabel : LabelBase
{
    private static string InitPower => AppIcons.Power + "≈ --.- W";

    /// <summary>底噪自动值（按机型默认，并在电池放电时自动校准）。</summary>
    private const double AutoIdleWatts = -1;

    /// <summary>底噪手动设置在配置文件中的键名。</summary>
    private const string IdleWattsKey = "idleWatts";

    /// <summary>底噪“自动”项的显示文本。</summary>
    private const string AutoIdleText = "自动（默认）";

    /// <summary>底噪常用档位：(显示文本, 瓦数)。</summary>
    private static readonly (string Text, double Value)[] IdleWattsQuickPicks =
    [
        ("8 W", 8),
        ("10 W（轻薄本）", 10),
        ("15 W（全能本）", 15),
        ("20 W", 20),
        ("30 W（台式机）", 30),
        ("45 W", 45),
        ("65 W", 65)
    ];

    /// <summary>底噪精细档位：(分组标题, 组内瓦数)，供二级菜单按 1 W 步进微调。</summary>
    private static readonly (string Group, double[] Values)[] IdleWattsFineGroups =
    [
        ("1 ~ 10 W", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
        ("11 ~ 20 W", [11, 12, 13, 14, 15, 16, 17, 18, 19, 20]),
        ("21 ~ 30 W", [21, 22, 23, 24, 25, 26, 27, 28, 29, 30]),
        ("31 ~ 40 W", [31, 32, 33, 34, 35, 36, 37, 38, 39, 40]),
        ("41 ~ 50 W", [41, 42, 43, 44, 45, 46, 47, 48, 49, 50]),
        ("51 ~ 60 W", [51, 52, 53, 54, 55, 56, 57, 58, 59, 60]),
        ("61 ~ 100 W", [65, 70, 75, 80, 85, 90, 95, 100])
    ];

    private volatile string _cachedPower = InitPower;

    private PowerMonitor? _monitor;
    private volatile PowerSample? _lastSample;
    private Timer? _sampler;
    private double _idleWattsOverride = AutoIdleWatts;
    private double _defaultIdleWatts = 30.0;

    /// <summary>上一次显示的功率、来源与图标模式：都未变时复用缓存的显示文本，避免每秒重复格式化。</summary>
    private double _lastShownWatts = double.NaN;

    private bool _lastShownEstimated;
    private bool _lastShownPlainText;

    /// <summary>采样重入保护：Timer 不保证回调不重叠，而 PowerMonitor.Sample() 必须串行调用。</summary>
    private int _sampling;

    public override void Init()
    {
        base.Init();
        Text.Text = InitPower;
        Text.Foreground = Brushes.Orange;
        Text.Width = 100;
    }

    public override void Update()
    {
        Text.Text = _cachedPower;
    }

    protected override void StartOnce()
    {
        _sampler = new Timer(_ => CalculatePower(), null, 0, 1000);
    }

    public override void OnStop()
    {
        _sampler?.Dispose();
        _sampler = null;
        _monitor?.Dispose();
        _monitor = null;
        base.OnStop();
    }

    public override void AddUiMenuItem(ContextMenu menu)
    {
        menu.Items.Add(new Separator());
        var powerMenu = new MenuItem { Header = "功耗 >>> " };
        BuildDropDownWpf(powerMenu, () => menu.IsOpen = false);
        menu.Items.Add(powerMenu);
    }

    public override void AddTrayMenuItem(WF.ContextMenuStrip menu)
    {
        menu.Items.Add(new WF.ToolStripSeparator());
        var powerMenu = new WF.ToolStripMenuItem("功耗 >>> ");
        BuildDropDownTray(powerMenu);
        menu.Items.Add(powerMenu);
    }

    public override void OnLoadConfig(JsonObject config)
    {
        if (config[IdleWattsKey] is not JsonValue value) return;

        double watts;
        if (value.TryGetValue<double>(out var d)) watts = d;
        else if (value.TryGetValue<int>(out var i)) watts = i;
        else return;

        if (watts is <= 0 or > 200) return;
        _idleWattsOverride = watts;
        ApplyIdleWatts();
    }

    public override void OnSaveConfig(JsonObject config)
    {
        if (_idleWattsOverride > 0) config[IdleWattsKey] = _idleWattsOverride;
    }

    private void CalculatePower()
    {
        // Timer 不保证回调不重叠：若上一次采样超过 1 秒仍未结束，本轮直接跳过，
        // 保证 PowerMonitor.Sample() 始终被单线程串行调用（其 EMA/校准/MSR 差分均非线程安全）。
        if (Interlocked.Exchange(ref _sampling, 1) == 1) return;

        try
        {
            // 监控对象惰性创建（RAPL 自检含约 400ms 等待，避免阻塞 UI 线程）；创建后长期复用
            _monitor ??= CreateMonitor();

            var sample = _monitor.Sample();
            _lastSample = sample;

            // 功率、来源与图标模式都未变化时复用上一秒的显示文本，避免重复格式化与无谓的 UI 赋值
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (sample.Watts == _lastShownWatts && sample.IsEstimated == _lastShownEstimated
                && AppIcons.PlainText == _lastShownPlainText) return;

            _lastShownWatts = sample.Watts;
            _lastShownEstimated = sample.IsEstimated;
            _lastShownPlainText = AppIcons.PlainText;
            _cachedPower = FormatPower(sample);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"功耗采样失败: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    private PowerMonitor CreateMonitor()
    {
        var monitor = new PowerMonitor();
        _defaultIdleWatts = monitor.IdleWatts; // 机型默认底噪（有电池 10W / 无电池 30W）
        ApplyIdleWatts(monitor);
        return monitor;
    }

    private void ApplyIdleWatts()
    {
        if (_monitor is not null) ApplyIdleWatts(_monitor);
    }

    private void ApplyIdleWatts(PowerMonitor monitor)
    {
        // 手动值为正时优先；否则回退到机型默认（电池放电时的自动校准仍会在 Sample() 内生效）
        monitor.IdleWatts = _idleWattsOverride > 0 ? _idleWattsOverride : _defaultIdleWatts;
    }

    private void ChangeIdleWatts(double watts, string text)
    {
        if (Math.Abs(watts - _idleWattsOverride) < 0.001) return;

        _idleWattsOverride = watts;
        ApplyIdleWatts();
        EventBus.Publish(EventType.ConfigChanged);
        WindowUtil.ShowToast("底噪功耗已设置为：" + text);
    }

    private void BuildDropDownWpf(MenuItem parent, Action closeMenu)
    {
        var sample = _lastSample;
        parent.Items.Add(InfoItemWpf("当前功耗：" + (sample is null ? InitPower : FormatPower(sample))));
        parent.Items.Add(InfoItemWpf("数据来源：" + DescribeSource(sample)));
        parent.Items.Add(InfoItemWpf($"CPU 负载：{(sample?.CpuLoadPct ?? 0):F0}%"));
        parent.Items.Add(InfoItemWpf("CPU 包功耗：" + FormatPkg(sample)));
        if (FormatBattery(sample) is { } batteryText) parent.Items.Add(InfoItemWpf(batteryText));

        parent.Items.Add(new Separator());
        parent.Items.Add(InfoItemWpf("当前底噪：" + IdleWattsText));
        AddIdleWattsMenuWpf(parent);

        parent.Items.Add(new Separator());
        parent.Items.Add(InfoItemWpf("RAPL 状态：" + RaplStatusText));

        var retryItem = new MenuItem { Header = "重新检测 CPU 功耗（RAPL）" };
        retryItem.Click += (_, _) =>
        {
            RetryRapl();
            closeMenu();
        };
        parent.Items.Add(retryItem);

        if (_monitor is not { RaplAvailable: false }) return;

        var downloadItem = new MenuItem { Header = "打开 PawnIO 下载页" };
        downloadItem.Click += (_, _) => OpenUrl(RaplReader.DriverDownloadUrl);
        parent.Items.Add(downloadItem);

        var copyItem = new MenuItem { Header = "复制 PawnIO 安装命令" };
        copyItem.Click += (_, _) => CopyInfo(RaplReader.DriverInstallCommand);
        parent.Items.Add(copyItem);
    }

    private void BuildDropDownTray(WF.ToolStripMenuItem parent)
    {
        var sample = _lastSample;
        parent.DropDownItems.Add(InfoItemTray("当前功耗：" + (sample is null ? InitPower : FormatPower(sample))));
        parent.DropDownItems.Add(InfoItemTray("数据来源：" + DescribeSource(sample)));
        parent.DropDownItems.Add(InfoItemTray($"CPU 负载：{(sample?.CpuLoadPct ?? 0):F0}%"));
        parent.DropDownItems.Add(InfoItemTray("CPU 包功耗：" + FormatPkg(sample)));
        if (FormatBattery(sample) is { } batteryText) parent.DropDownItems.Add(InfoItemTray(batteryText));

        parent.DropDownItems.Add(new WF.ToolStripSeparator());
        parent.DropDownItems.Add(InfoItemTray("当前底噪：" + IdleWattsText));
        AddIdleWattsMenuTray(parent);

        parent.DropDownItems.Add(new WF.ToolStripSeparator());
        parent.DropDownItems.Add(InfoItemTray("RAPL 状态：" + RaplStatusText));

        var retryItem = new WF.ToolStripMenuItem("重新检测 CPU 功耗（RAPL）");
        retryItem.Click += (_, _) => RetryRapl();
        parent.DropDownItems.Add(retryItem);

        if (_monitor is not { RaplAvailable: false }) return;

        var downloadItem = new WF.ToolStripMenuItem("打开 PawnIO 下载页");
        downloadItem.Click += (_, _) => OpenUrl(RaplReader.DriverDownloadUrl);
        parent.DropDownItems.Add(downloadItem);

        var copyItem = new WF.ToolStripMenuItem("复制 PawnIO 安装命令");
        copyItem.Click += (_, _) => CopyInfo(RaplReader.DriverInstallCommand);
        parent.DropDownItems.Add(copyItem);
    }

    private string RaplStatusText => _monitor?.RaplStatus ?? "初始化中";

    private string IdleWattsText => _monitor is null
        ? "初始化中"
        : _idleWattsOverride > 0
            ? $"{_idleWattsOverride:0.#} W（手动）"
            : $"自动（当前 {_monitor.IdleWatts:0.#} W）";

    private void AddIdleWattsMenuWpf(MenuItem parent)
    {
        // 统一缩进到父菜单“底噪设置”：自动 + 常用档位 + 精细档位分组（分组展开后为 1 W 步进档位）
        var idleMenu = new MenuItem { Header = "底噪设置 >>> " };

        var autoItem = new MenuItem
        {
            Header = AutoIdleText,
            IsCheckable = true,
            IsChecked = _idleWattsOverride <= 0
        };
        autoItem.Click += (_, _) => ChangeIdleWatts(AutoIdleWatts, AutoIdleText);
        idleMenu.Items.Add(autoItem);

        idleMenu.Items.Add(new Separator());

        var quickMenu = new MenuItem { Header = "常用档位 >>> " };
        foreach (var (text, value) in IdleWattsQuickPicks) quickMenu.Items.Add(CreateIdleWattsItemWpf(value, text));
        idleMenu.Items.Add(quickMenu);

        idleMenu.Items.Add(new Separator());

        foreach (var (group, values) in IdleWattsFineGroups)
        {
            var groupMenu = new MenuItem { Header = group + " >>> " };
            foreach (var value in values) groupMenu.Items.Add(CreateIdleWattsItemWpf(value, $"{value:0.#} W"));
            idleMenu.Items.Add(groupMenu);
        }

        parent.Items.Add(idleMenu);
    }

    private MenuItem CreateIdleWattsItemWpf(double value, string text)
    {
        var item = new MenuItem
        {
            Header = text,
            IsCheckable = true,
            IsChecked = Math.Abs(value - _idleWattsOverride) < 0.001
        };
        item.Click += (_, _) => ChangeIdleWatts(value, text);
        return item;
    }

    private void AddIdleWattsMenuTray(WF.ToolStripMenuItem parent)
    {
        var idleMenu = new WF.ToolStripMenuItem("底噪设置 >>> ");

        var autoItem = new WF.ToolStripMenuItem(AutoIdleText) { Checked = _idleWattsOverride <= 0 };
        autoItem.Click += (_, _) => ChangeIdleWatts(AutoIdleWatts, AutoIdleText);
        idleMenu.DropDownItems.Add(autoItem);

        idleMenu.DropDownItems.Add(new WF.ToolStripSeparator());

        var quickMenu = new WF.ToolStripMenuItem("常用档位 >>> ");
        foreach (var (text, value) in IdleWattsQuickPicks)
            quickMenu.DropDownItems.Add(CreateIdleWattsItemTray(value, text));
        idleMenu.DropDownItems.Add(quickMenu);

        idleMenu.DropDownItems.Add(new WF.ToolStripSeparator());

        foreach (var (group, values) in IdleWattsFineGroups)
        {
            var groupMenu = new WF.ToolStripMenuItem(group + " >>> ");
            foreach (var value in values) groupMenu.DropDownItems.Add(CreateIdleWattsItemTray(value, $"{value:0.#} W"));
            idleMenu.DropDownItems.Add(groupMenu);
        }

        parent.DropDownItems.Add(idleMenu);
    }

    private WF.ToolStripMenuItem CreateIdleWattsItemTray(double value, string text)
    {
        var item = new WF.ToolStripMenuItem(text) { Checked = Math.Abs(value - _idleWattsOverride) < 0.001 };
        item.Click += (_, _) => ChangeIdleWatts(value, text);
        return item;
    }

    private static string FormatPower(PowerSample sample)
        => AppIcons.Power + (sample.IsEstimated ? "≈" : "") + $"{sample.Watts:F1} W";

    private static string DescribeSource(PowerSample? sample)
        => sample is null ? "初始化中"
            : sample.IsEstimated ? "估算（底噪 + CPU）" : "实测（电池放电功率）";

    private static string FormatPkg(PowerSample? sample)
        => sample?.PkgWatts is { } pkg ? $"{pkg:F1} W（RAPL 实测）" : "不可用（已回退估算）";

    private static string? FormatBattery(PowerSample? sample)
    {
        if (sample?.BatteryPct is not { } pct) return null;

        string extra;
        if (sample.AcOnline) extra = "（接通电源）";
        else if (sample.RemainingMinutes is { } min and > 0) extra = $"（放电中，剩余约 {min:F0} 分钟）";
        else extra = "（放电中）";

        return $"电池：{pct:F0}% {extra}";
    }

    private void RetryRapl()
    {
        if (_monitor is null)
        {
            WindowUtil.ShowToast("功耗监控尚未初始化");
            return;
        }

        _monitor.RetryRapl();
        WindowUtil.ShowToast("RAPL 重新检测结果：" + _monitor.RaplStatus);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"打开链接失败: {ex.Message}");
        }
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
}