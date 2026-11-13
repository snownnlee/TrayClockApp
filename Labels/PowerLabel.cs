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

    /// <summary>底噪“自动”哨兵值（≤ 0）：交回机型默认，并在电池放电时自动校准。</summary>
    private const double AutoIdleWatts = Constants.DefaultIdleWatts;

    /// <summary>底噪手动设置在配置文件中的键名。</summary>
    private const string IdleWattsKey = "idleWatts";

    /// <summary>放电校准结果的持久化键名（重启后直接沿用，避免每次都从机型默认重新学习）。</summary>
    private const string CalibratedIdleWattsKey = "calibratedIdleWatts";

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

    /// <summary>上次落盘的放电校准底噪（≤ 0 表示没有），监控对象创建时写入。</summary>
    private double _savedCalibratedIdle = AutoIdleWatts;

    /// <summary>已落盘的校准值快照，用于节流判断（避免每帧都写配置文件）。</summary>
    private double _persistedCalibratedIdle = AutoIdleWatts;

    /// <summary>上次因校准值变化触发保存的时刻（Environment.TickCount64，0 = 尚未保存过）。</summary>
    private long _lastCalibrationSaveTick;

    /// <summary>上一次显示的功率、底噪、来源与图标模式：都未变时复用缓存的显示文本，避免每秒重复格式化。</summary>
    private double _lastShownWatts = double.NaN;

    private double _lastShownIdleWatts = double.NaN;
    private bool _lastShownEstimated;
    private bool _lastShownPlainText;

    /// <summary>采样重入保护：Timer 不保证回调不重叠，而 PowerMonitor.Sample() 必须串行调用。</summary>
    private int _sampling;

    public override void Init()
    {
        base.Init();
        Text.Text = InitPower;
        Text.Foreground = Brushes.Yellow;

        // 宽度按“💡≈ 999.9 W (999 W)”并兼顾纯文本图标模式（“[P] ≈ 999.9 W (999 W)”）估算，
        // 固定宽度可避免数字位数变化时整条悬浮条抖动，同时留足左右边距不贴边
        Text.Width = 175;
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
        if (ReadWatts(config[IdleWattsKey]) is { } manual && manual is > 0 and <= 200)
        {
            _idleWattsOverride = manual;
            ApplyIdleWatts();
        }

        if (ReadWatts(config[CalibratedIdleWattsKey]) is { } calibrated)
        {
            _savedCalibratedIdle = calibrated;
            _persistedCalibratedIdle = calibrated; // 已落盘过的值，启动后不必再重写一次
        }
    }

    public override void OnSaveConfig(JsonObject config)
    {
        if (_idleWattsOverride > 0) config[IdleWattsKey] = _idleWattsOverride;

        if (_monitor is { } monitor && monitor.CalibratedIdleWatts > 0)
            config[CalibratedIdleWattsKey] = Math.Round(monitor.CalibratedIdleWatts, 2);
    }

    /// <summary>读取配置里的瓦数（JSON 里可能被写成整数或小数）。</summary>
    private static double? ReadWatts(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var d)) return d;
        return value.TryGetValue<int>(out var i) ? i : null;
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
            MaybePersistCalibratedIdle();

            var idleWatts = _monitor.EffectiveIdleWatts;

            // 功率、底噪、来源与图标模式都未变化时复用上一秒的显示文本，避免重复格式化与无谓的 UI 赋值
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (sample.Watts == _lastShownWatts && sample.IsEstimated == _lastShownEstimated
                && AppIcons.PlainText == _lastShownPlainText
                && Math.Abs(idleWatts - _lastShownIdleWatts) < 0.05) return;

            _lastShownWatts = sample.Watts;
            _lastShownEstimated = sample.IsEstimated;
            _lastShownPlainText = AppIcons.PlainText;
            _lastShownIdleWatts = idleWatts;
            _cachedPower = FormatPowerWithIdle(sample, idleWatts);
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

        // 沿用上次落盘的放电校准值（本次放电时会在此基础上继续慢速收敛）
        if (_savedCalibratedIdle > 0) monitor.CalibratedIdleWatts = _savedCalibratedIdle;

        ApplyIdleWatts(monitor);
        return monitor;
    }

    private void ApplyIdleWatts()
    {
        if (_monitor is not null) ApplyIdleWatts(_monitor);
    }

    private void ApplyIdleWatts(PowerMonitor monitor)
    {
        // 手动值为正时锁定；否则交回自动（放电校准 &gt; 机型默认）
        monitor.ManualIdleWatts = _idleWattsOverride > 0 ? _idleWattsOverride : AutoIdleWatts;
    }

    /// <summary>
    /// 校准值节流落盘：窗口均值相对上次落盘值的变动超过阈值、且距上次保存超过最小间隔时才写一次配置，
    /// 避免放电期间每秒变化都触发整文件写入。首次学习到的校准值会立即保存。
    /// </summary>
    private void MaybePersistCalibratedIdle()
    {
        // 手动锁定期间校准值不更新，也没有保存的意义
        if (_monitor is not { AutoIdle: true } monitor) return;

        var calibrated = monitor.CalibratedIdleWatts;
        if (calibrated <= 0) return;

        if (Math.Abs(calibrated - _persistedCalibratedIdle) < Constants.IdleCalibrationSaveDeltaW) return;

        var now = Environment.TickCount64;
        if (_lastCalibrationSaveTick != 0
            && now - _lastCalibrationSaveTick < Constants.IdleCalibrationSaveIntervalS * 1000L) return;

        _persistedCalibratedIdle = calibrated;
        _lastCalibrationSaveTick = now;
        EventBus.Publish(EventType.ConfigChanged);
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

    /// <summary>显示真正参与估算的底噪及其来源（手动 / 放电校准 / 机型默认）。</summary>
    private string IdleWattsText => _monitor is null
        ? "初始化中"
        : _monitor.AutoIdle
            ? $"自动（当前 {_monitor.EffectiveIdleWatts:0.#} W，{_monitor.IdleWattsSource}）"
            : $"{_monitor.EffectiveIdleWatts:0.#} W（{_monitor.IdleWattsSource}）";

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

    /// <summary>
    /// 标签显示用：总功率后以括号附加底噪，如 “💡≈ 12.3 W (10 W)”。
    /// 全 ASCII，避免中文字形在 emoji 优先的字体链里渲染异常。
    /// </summary>
    private static string FormatPowerWithIdle(PowerSample sample, double idleWatts)
        => FormatPower(sample) + $" ({idleWatts:0.#} W)";

    private string DescribeSource(PowerSample? sample)
    {
        if (sample is null) return "初始化中";
        if (!sample.IsEstimated) return "实测（电池放电功率）";

        return _monitor is null
            ? "估算（底噪 + CPU）"
            : $"估算（底噪 {_monitor.EffectiveIdleWatts:0.#} W/{_monitor.IdleWattsSource} + CPU）";
    }

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