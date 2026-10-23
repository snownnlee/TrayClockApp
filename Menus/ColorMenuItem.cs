using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Forms;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus;

/// <summary>缓变配色：让标签文字色相沿色相环缓慢摆动，同时保留各自原色的饱和度与亮度。</summary>
public class ColorMenuItem : MenuItemBase
{
    private const string EnabledKey = "antiBurnInColor";
    private const string PeriodKey = "antiBurnInColorPeriodS";
    private const string GradientKey = "antiBurnInColorGradient";

    // 升级兼容：独立配置缺失时沿用拆分前共用的设置
    private const string FallbackPeriodKey = "antiBurnInPeriodS";
    private const string FallbackGradientKey = "antiBurnInGradient";

    private const string HeaderText = "缓变配色 >>> ";
    private const string EnabledText = "启用缓变配色";
    private const string PeriodHeaderText = "缓变周期 >>> ";
    private const string GradientHeaderText = "渐变模式 >>> ";

    private static readonly (string Text, int Value)[] PeriodList =
    [
        ("5 秒", 5),
        ("10 秒", 10),
        ("20 秒", 20),
        ("30 秒", 30),
        ("1 分钟", 60),
        ("2 分钟", 120),
        ("3 分钟", 180)
    ];

    private static readonly (GradientMode Mode, string Text)[] GradientList =
    [
        (GradientMode.Sine, "正弦渐变"),
        (GradientMode.Linear, "线性渐变")
    ];

    private bool _enabled = Constants.DefaultAntiBurnInColor;
    private int _periodS = Constants.DefaultAntiBurnInColorPeriodS;
    private GradientMode _gradient = Constants.DefaultAntiBurnInColorGradient;

    public override void AddUiMenuItem(ContextMenu menu)
    {
        menu.Items.Add(new Separator());

        var subMenu = new MenuItem { Header = HeaderText };
        AddUiSubMenu(subMenu);
        menu.Items.Add(subMenu);
    }

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var subMenu = new ToolStripMenuItem(HeaderText);
        AddTraySubMenu(subMenu);
        menu.Items.Add(subMenu);
    }

    /// <summary>把子项挂到指定的父菜单下，供防烧屏菜单组合</summary>
    public void AddUiSubMenu(MenuItem parent)
    {
        var enabledItem = new MenuItem
        {
            Header = EnabledText,
            IsCheckable = true,
            IsChecked = _enabled
        };
        enabledItem.Click += (_, _) => SetEnabled(enabledItem.IsChecked);
        parent.Items.Add(enabledItem);

        var periodMenu = new MenuItem { Header = PeriodHeaderText };
        foreach (var (text, value) in PeriodList)
        {
            var item = new MenuItem
            {
                Header = text,
                IsCheckable = true,
                IsChecked = value == _periodS
            };
            item.Click += (_, _) =>
            {
                if (value != _periodS) SetPeriod(value, text);
            };
            periodMenu.Items.Add(item);
        }

        parent.Items.Add(periodMenu);

        var gradientMenu = new MenuItem { Header = GradientHeaderText };
        foreach (var (mode, text) in GradientList)
        {
            var item = new MenuItem
            {
                Header = text,
                IsCheckable = true,
                IsChecked = mode == _gradient
            };
            item.Click += (_, _) =>
            {
                if (mode != _gradient) SetGradient(mode, text);
            };
            gradientMenu.Items.Add(item);
        }

        parent.Items.Add(gradientMenu);
    }

    /// <summary>把子项挂到指定的父菜单下，供防烧屏菜单组合</summary>
    public void AddTraySubMenu(ToolStripMenuItem parent)
    {
        var enabledItem = new ToolStripMenuItem(EnabledText)
        {
            Checked = _enabled,
            CheckOnClick = true
        };
        enabledItem.CheckedChanged += (_, _) => SetEnabled(enabledItem.Checked);
        parent.DropDownItems.Add(enabledItem);

        var periodMenu = new ToolStripMenuItem(PeriodHeaderText);
        foreach (var (text, value) in PeriodList)
        {
            var item = new ToolStripMenuItem(text) { Checked = value == _periodS };
            item.Click += (_, _) =>
            {
                if (value != _periodS) SetPeriod(value, text);
            };
            periodMenu.DropDownItems.Add(item);
        }

        parent.DropDownItems.Add(periodMenu);

        var gradientMenu = new ToolStripMenuItem(GradientHeaderText);
        foreach (var (mode, text) in GradientList)
        {
            var item = new ToolStripMenuItem(text) { Checked = mode == _gradient };
            item.Click += (_, _) =>
            {
                if (mode != _gradient) SetGradient(mode, text);
            };
            gradientMenu.DropDownItems.Add(item);
        }

        parent.DropDownItems.Add(gradientMenu);
    }

    public override void OnLoadConfig(JsonObject config)
    {
        if (config[EnabledKey] is JsonValue enabledValue && enabledValue.TryGetValue<bool>(out var enabled))
            _enabled = enabled;

        var periodNode = config[PeriodKey] ?? config[FallbackPeriodKey];
        if (periodNode is JsonValue periodValue && periodValue.TryGetValue<int>(out var periodS))
            _periodS = Math.Clamp(periodS, Constants.MinAntiBurnInPeriodS, Constants.MaxAntiBurnInPeriodS);

        var gradientNode = config[GradientKey] ?? config[FallbackGradientKey];
        if (gradientNode is JsonValue gradientValue && gradientValue.TryGetValue<string>(out var gradientText)
            && Enum.TryParse(gradientText, ignoreCase: true, out GradientMode gradient))
            _gradient = gradient;

        Window!.SetColorBreathing(_enabled, _periodS, _gradient);
    }

    public override void OnSaveConfig(JsonObject config)
    {
        config[EnabledKey] = _enabled;
        config[PeriodKey] = _periodS;
        config[GradientKey] = _gradient.ToString().ToLowerInvariant();
    }

    public override void OnStop()
    {
        Window!.SetColorBreathing(false, _periodS, _gradient);
    }

    private void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;

        _enabled = enabled;
        Apply();
        WindowUtil.ShowToast(enabled ? "缓变配色已启用" : "缓变配色已禁用");
    }

    private void SetPeriod(int periodS, string text)
    {
        _periodS = periodS;
        Apply();
        WindowUtil.ShowToast("配色周期已切换为：" + text);
    }

    private void SetGradient(GradientMode mode, string text)
    {
        _gradient = mode;
        Apply();
        WindowUtil.ShowToast("配色渐变模式已切换为：" + text);
    }

    private void Apply()
    {
        Window!.SetColorBreathing(_enabled, _periodS, _gradient);
        EventBus.Publish(EventType.ConfigChanged);
    }
}
