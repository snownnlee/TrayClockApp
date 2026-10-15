using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Forms;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus;

public class AntiBurnInMenuItem : MenuItemBase
{
    private const string EnabledKey = "antiBurnIn";
    private const string AmplitudeKey = "antiBurnInAmplitudeN";
    private const string PeriodKey = "antiBurnInPeriodS";
    private const string GradientKey = "antiBurnInGradient";

    private const string HeaderText = "防烧屏 >>> ";
    private const string EnabledText = "启用缓变亮度";
    private const string GradientHeaderText = "渐变模式 >>> ";

    private static readonly int[] AmplitudeList = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

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

    private bool _enabled = Constants.DefaultAntiBurnIn;
    private int _amplitudeN = Constants.DefaultAntiBurnInAmplitudeN;
    private int _periodS = Constants.DefaultAntiBurnInPeriodS;
    private GradientMode _gradient = Constants.DefaultAntiBurnInGradient;

    public override void AddUiMenuItem(ContextMenu menu)
    {
        menu.Items.Add(new Separator());

        var antiBurnInMenu = new MenuItem { Header = HeaderText };

        var enabledItem = new MenuItem
        {
            Header = EnabledText,
            IsCheckable = true,
            IsChecked = _enabled
        };
        enabledItem.Click += (_, _) => SetEnabled(enabledItem.IsChecked);
        antiBurnInMenu.Items.Add(enabledItem);

        var periodMenu = new MenuItem { Header = "缓变周期 >>> " };
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

        antiBurnInMenu.Items.Add(periodMenu);
        
        var amplitudeMenu = new MenuItem { Header = "亮度降幅 >>> " };
        foreach (var amplitude in AmplitudeList)
        {
            var text = $"{amplitude}%";
            var item = new MenuItem
            {
                Header = text,
                IsCheckable = true,
                IsChecked = amplitude == _amplitudeN
            };
            item.Click += (_, _) =>
            {
                if (amplitude != _amplitudeN) SetAmplitude(amplitude, text);
            };
            amplitudeMenu.Items.Add(item);
        }

        antiBurnInMenu.Items.Add(amplitudeMenu);

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

        antiBurnInMenu.Items.Add(gradientMenu);

        menu.Items.Add(antiBurnInMenu);
    }

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var antiBurnInMenu = new ToolStripMenuItem(HeaderText);

        var enabledItem = new ToolStripMenuItem(EnabledText)
        {
            Checked = _enabled,
            CheckOnClick = true
        };
        enabledItem.CheckedChanged += (_, _) => SetEnabled(enabledItem.Checked);
        antiBurnInMenu.DropDownItems.Add(enabledItem);
        
        var periodMenu = new ToolStripMenuItem("缓变周期 >>> ");
        foreach (var (text, value) in PeriodList)
        {
            var item = new ToolStripMenuItem(text) { Checked = value == _periodS };
            item.Click += (_, _) =>
            {
                if (value != _periodS) SetPeriod(value, text);
            };
            periodMenu.DropDownItems.Add(item);
        }

        antiBurnInMenu.DropDownItems.Add(periodMenu);

        var amplitudeMenu = new ToolStripMenuItem("亮度降幅 >>> ");
        foreach (var amplitude in AmplitudeList)
        {
            var text = $"{amplitude}%";
            var item = new ToolStripMenuItem(text) { Checked = amplitude == _amplitudeN };
            item.Click += (_, _) =>
            {
                if (amplitude != _amplitudeN) SetAmplitude(amplitude, text);
            };
            amplitudeMenu.DropDownItems.Add(item);
        }

        antiBurnInMenu.DropDownItems.Add(amplitudeMenu);

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

        antiBurnInMenu.DropDownItems.Add(gradientMenu);

        menu.Items.Add(antiBurnInMenu);
    }

    public override void OnLoadConfig(JsonObject config)
    {
        if (config[EnabledKey] is JsonValue enabledValue && enabledValue.TryGetValue<bool>(out var enabled))
            _enabled = enabled;

        if (config[AmplitudeKey] is JsonValue amplitudeValue && amplitudeValue.TryGetValue<int>(out var amplitudeN))
            _amplitudeN = Math.Clamp(amplitudeN, 0, Constants.MaxAmplitudeN);

        if (config[PeriodKey] is JsonValue periodValue && periodValue.TryGetValue<int>(out var periodS))
            _periodS = Math.Clamp(periodS, Constants.MinAntiBurnInPeriodS, Constants.MaxAntiBurnInPeriodS);

        if (config[GradientKey] is JsonValue gradientValue && gradientValue.TryGetValue<string>(out var gradientText)
            && Enum.TryParse(gradientText, ignoreCase: true, out GradientMode gradient))
            _gradient = gradient;

        Window!.SetAntiBurnIn(_enabled, _amplitudeN, _periodS, _gradient);
    }

    public override void OnSaveConfig(JsonObject config)
    {
        config[EnabledKey] = _enabled;
        config[AmplitudeKey] = _amplitudeN;
        config[PeriodKey] = _periodS;
        config[GradientKey] = _gradient.ToString().ToLowerInvariant();
    }

    public override void OnStop()
    {
        Window!.SetAntiBurnIn(false, 0, _periodS, _gradient);
    }

    private void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;

        _enabled = enabled;
        Apply();
        WindowUtil.ShowToast(enabled ? "防烧屏缓变亮度已启用" : "防烧屏缓变亮度已禁用");
    }

    private void SetAmplitude(int amplitudeN, string text)
    {
        _amplitudeN = amplitudeN;
        Apply();
        WindowUtil.ShowToast("亮度降幅已切换为：" + text);
    }

    private void SetPeriod(int periodS, string text)
    {
        _periodS = periodS;
        Apply();
        WindowUtil.ShowToast("缓变周期已切换为：" + text);
    }

    private void SetGradient(GradientMode mode, string text)
    {
        _gradient = mode;
        Apply();
        WindowUtil.ShowToast("渐变模式已切换为：" + text);
    }

    private void Apply()
    {
        Window!.SetAntiBurnIn(_enabled, _amplitudeN, _periodS, _gradient);
        EventBus.Publish(EventType.ConfigChanged);
    }
}
