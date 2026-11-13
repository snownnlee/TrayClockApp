using System.IO;

namespace TrayClockApp.Core;

/// <summary>
/// 全局常量。菜单项的默认值集中在本文件“菜单默认值”区域，按右键菜单的出现顺序分组，
/// 调整默认值只需改这里（配置文件里已保存的项会覆盖默认值，删掉对应键即可恢复）。
/// </summary>
public static class Constants
{
    // ==================== 字体 ====================

    public const string EmojiFont =
        "Segoe UI Emoji, Segoe UI Symbol, Segoe UI, Noto Emoji, Noto Sans Symbols 2, Symbola, DejaVu Sans";

    // ==================== 菜单默认值（按右键菜单顺序） ====================
    // 约定：每一项默认值都标注所属菜单路径与配置文件键名，括号内为键名。
    // 菜单顺序：显示/隐藏 · 置顶 · 交互 · 界面 · 防烧屏 · 不透明度 · 标签组件 · 更多 · 关于 · 重启 · 退出

    // ---- 置顶（ToFrontMenuItem）----

    /// <summary>置顶 &gt;&gt;&gt; 自动检测置顶：默认是否自动保持窗口处于最前。配置键 autoToFront。</summary>
    public const bool DefaultAutoToFrontOn = true;

    // ---- 交互（InteractionMenuItem）----

    /// <summary>交互：默认可否用鼠标操作窗口（false 时窗口鼠标穿透）。配置键 interaction。</summary>
    public const bool DefaultInteraction = true;

    // ---- 界面（UiMenuItem）----

    /// <summary>界面 &gt;&gt;&gt; 启用/禁用拖拽：默认是否允许拖动窗口位置。配置键 draggable。</summary>
    public const bool DefaultDraggable = true;

    /// <summary>界面 &gt;&gt;&gt; 纯文本图标模式：默认图标模式（false 用 emoji；系统缺少 emoji 字体时会自动回退为纯文本）。配置键 plainTextIcons。</summary>
    public const bool DefaultPlainTextIcons = false;

    // ---- 防烧屏 &gt;&gt;&gt; 缓变亮度（BrightnessMenuItem）----

    /// <summary>缓变亮度：默认是否启用。配置键 antiBurnIn。</summary>
    public const bool DefaultAntiBurnIn = false;

    /// <summary>缓变亮度 &gt;&gt;&gt; 亮度降幅：默认降幅百分比（0 ~ <see cref="MaxAmplitudeN"/>）。配置键 antiBurnInAmplitudeN。</summary>
    public const int DefaultAntiBurnInAmplitudeN = 60;

    /// <summary>缓变亮度 &gt;&gt;&gt; 缓变周期：默认周期秒数（<see cref="MinAntiBurnInPeriodS"/> ~ <see cref="MaxAntiBurnInPeriodS"/>）。配置键 antiBurnInPeriodS。</summary>
    public const int DefaultAntiBurnInPeriodS = 10;

    /// <summary>缓变亮度 &gt;&gt;&gt; 渐变模式：默认渐变曲线。配置键 antiBurnInGradient。</summary>
    public const GradientMode DefaultAntiBurnInGradient = GradientMode.Linear;

    // ---- 防烧屏 &gt;&gt;&gt; 缓变配色（ColorMenuItem）----

    /// <summary>缓变配色：默认是否启用。配置键 antiBurnInColor。</summary>
    public const bool DefaultAntiBurnInColor = true;

    /// <summary>缓变配色 &gt;&gt;&gt; 缓变周期：默认周期秒数（<see cref="MinAntiBurnInPeriodS"/> ~ <see cref="MaxAntiBurnInPeriodS"/>）。配置键 antiBurnInColorPeriodS。</summary>
    public const int DefaultAntiBurnInColorPeriodS = 20;

    /// <summary>缓变配色 &gt;&gt;&gt; 渐变模式：默认渐变曲线。配置键 antiBurnInColorGradient。</summary>
    public const GradientMode DefaultAntiBurnInColorGradient = GradientMode.Linear;

    // ---- 不透明度（OpacityMenuItem）----

    /// <summary>不透明度：默认百分比（0 ~ 100，取 0 时按 1 处理以免完全不可见）。配置键 opacityN。</summary>
    public const int DefaultOpacity = 50;

    // ---- 标签组件（LabelComponentMenuItem）----

    /// <summary>
    /// 功耗 &gt;&gt;&gt; 底噪设置：默认底噪瓦数，≤ 0 表示“自动”（按机型默认底噪并在电池放电时校准）。
    /// 配置键 idleWatts；自动模式下校准结果另存于 calibratedIdleWatts，重启后沿用。
    /// </summary>
    public const double DefaultIdleWatts = -1;

    /// <summary>
    /// 功耗 &gt;&gt;&gt; 底噪设置：放电校准的时间窗口（秒）。
    /// 窗口内每帧的 (整机放电功率 − CPU 包功耗) 取算术平均，切到插电后就用该均值参与估算，
    /// 避免单个瞬时样本的波动直接带进插电估算。
    /// </summary>
    public const int IdleCalibrationWindowS = 60;

    /// <summary>
    /// 功耗 &gt;&gt;&gt; 底噪设置：校准样本的相邻帧功率跳变阈值（W）。
    /// 整机功率或 CPU 包功耗相对上一帧跳变超过该值（负载切换瞬间）时，该帧不并入窗口统计，
    /// 避免 EMA 滞后与负载突变叠加产生失真的 baseline 样本。
    /// </summary>
    public const double IdleCalibrationMaxStepW = 3.0;

    /// <summary>
    /// 功耗 &gt;&gt;&gt; 底噪设置：校准样本允许的最大 CPU 负载（0~100）。
    /// 底噪（整机 − CPU 包）会随 CPU 功率上升而升高（RAPL 不含供电/VRM 损耗），
    /// 因此只采低负载样本，避免窗口均值被高负载期拉高、进而与“底噪 + CPU 部分”的估算模型冲突。
    /// </summary>
    public const int IdleCalibrationMaxCpuLoadPct = 20;

    /// <summary>
    /// 功耗 &gt;&gt;&gt; 底噪设置：校准样本的稳态门槛（连续满足条件的帧数，约 1 帧/秒）。
    /// 电池放电功率上报有 1~3 秒滞后：负载刚回落时 raw 仍偏高而 CPU 包已降，
    /// 需连续稳定若干帧后再采样，避免把过渡期的假高 baseline 计入窗口。
    /// </summary>
    public const int IdleCalibrationSettleFrames = 5;

    /// <summary>
    /// 功耗 &gt;&gt;&gt; 底噪设置：校准值节流保存的变动阈值（W）。
    /// 窗口均值相对上次落盘值变化超过该阈值、且已超过下面的间隔时间，才会写一次配置。
    /// </summary>
    public const double IdleCalibrationSaveDeltaW = 0.5;

    /// <summary>功耗 &gt;&gt;&gt; 底噪设置：校准值节流保存的最小间隔（秒）。</summary>
    public const int IdleCalibrationSaveIntervalS = 30;

    // 其余标签项默认均为“自动”，不设常量：
    //   网络接口 —— 取首个可用网络接口；电源 —— 取首个检测到的电源。
    //
    // 更多（MoreMenuItem）下的“保持屏幕常亮 / 保持系统运行”互斥单选、默认都不启用且不写入配置，
    // “注册开机启动”以系统计划任务的实际状态为准，故无默认值常量。

    // ==================== 取值范围 ====================

    public const int MaxAmplitudeN = 100;
    public const int MinAntiBurnInPeriodS = 5;
    public const int MaxAntiBurnInPeriodS = 3600;

    // ==================== 布局尺寸 ====================

    public const int DefaultComponentHeight = 25;

    // ==================== 数据尺寸 ====================

    public const long DataSize1Kb = 1024;
    public const long DataSize1Mb = 1024 * 1024;
    public const long DataSize1Gb = 1024 * 1024 * 1024;

    // ==================== 配置路径 ====================

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TrayClockApp");

    public static string ConfigFile => Path.Combine(ConfigDir, "config.json");
}
