using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TrayClockApp.Core;

namespace TrayClockApp.Infra;

/// <summary>
/// 让各标签文字的色相沿色相环缓变，同时保留各自原色的饱和度、亮度与相对色差。
/// 通过让像素点在色相环上移动，避免同一位置的子像素长期承受相同的发光衰减。
/// </summary>
public sealed class ColorBreathing
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>一个周期内的色相摆动总角度</summary>
    private const double HueSweepDegrees = 360.0;

    private static readonly DependencyPropertyDescriptor ForegroundDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.ForegroundProperty, typeof(TextBlock))!;

    private readonly Panel _host;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<TextBlock, Entry> _entries = [];

    private double _phase;
    private double _hueShift;
    private bool _applying;
    private int _periodS = Constants.DefaultAntiBurnInColorPeriodS;
    private GradientMode _gradient = Constants.DefaultAntiBurnInGradient;

    public ColorBreathing(Panel host)
    {
        _host = host;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Advance();
    }

    public void Configure(bool enabled, int periodS, GradientMode gradient)
    {
        _periodS = Math.Clamp(periodS, Constants.MinAntiBurnInPeriodS, Constants.MaxAntiBurnInPeriodS);
        _gradient = gradient;
        _phase = 0;

        if (!enabled)
        {
            Stop();
            return;
        }

        if (!_timer.IsEnabled) _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Restore();
    }

    private void Advance()
    {
        _phase += TickInterval.TotalSeconds / _periodS;
        if (_phase >= 1.0) _phase -= 1.0;

        _hueShift = BreathingCurve.Factor(_gradient, _phase) * HueSweepDegrees;

        foreach (var text in _host.Children.OfType<TextBlock>()) Apply(text);
    }

    private void Apply(TextBlock text)
    {
        if (!_entries.TryGetValue(text, out var entry))
        {
            entry = new Entry(text.Foreground);
            _entries[text] = entry;
            ForegroundDescriptor.AddValueChanged(text, OnForegroundChanged);
        }
        else if (!ReferenceEquals(text.Foreground, entry.Applied))
        {
            entry.Base = text.Foreground;
        }

        ApplyShift(text, entry);
    }

    /// <summary>
    /// 标签自行改色（如电池状态每秒刷新）时立即按当前相位重算，
    /// 避免原色在两次定时器 Tick 之间短暂闪现造成闪烁。
    /// </summary>
    private void OnForegroundChanged(object? sender, EventArgs e)
    {
        if (_applying || !_timer.IsEnabled) return;
        if (sender is not TextBlock text) return;
        if (!_entries.TryGetValue(text, out var entry)) return;

        entry.Base = text.Foreground;
        ApplyShift(text, entry);
    }

    private void ApplyShift(TextBlock text, Entry entry)
    {
        if (entry.Base is not SolidColorBrush baseBrush) return;

        var brush = new SolidColorBrush(ShiftHue(baseBrush.Color, _hueShift));
        brush.Freeze();
        entry.Applied = brush;

        _applying = true;
        text.Foreground = brush;
        _applying = false;
    }

    private void Restore()
    {
        foreach (var (text, entry) in _entries)
        {
            ForegroundDescriptor.RemoveValueChanged(text, OnForegroundChanged);

            if (entry.Applied is not null && ReferenceEquals(text.Foreground, entry.Applied))
                text.Foreground = entry.Base;
        }

        _entries.Clear();
    }

    /// <summary>保持饱和度与亮度不变，仅把色相平移指定角度</summary>
    private static Color ShiftHue(Color color, double degrees)
    {
        var (h, s, l) = ToHsl(color);
        h = (h + degrees / 360.0) % 1.0;
        if (h < 0) h += 1.0;

        var (r, g, b) = FromHsl(h, s, l);
        return Color.FromArgb(color.A, r, g, b);
    }

    private static (double H, double S, double L) ToHsl(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var delta = max - min;

        if (delta <= 0) return (0, 0, l);

        var s = l > 0.5 ? delta / (2 - max - min) : delta / (max + min);

        var h = r >= g && r >= b
            ? ((g - b) / delta + (g < b ? 6 : 0)) / 6
            : g >= b
                ? ((b - r) / delta + 2) / 6
                : ((r - g) / delta + 4) / 6;

        return (h, s, l);
    }

    private static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        if (s == 0)
        {
            var v = ToByte(l);
            return (v, v, v);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        return (ToByte(HueToRgb(p, q, h + 1.0 / 3)), ToByte(HueToRgb(p, q, h)), ToByte(HueToRgb(p, q, h - 1.0 / 3)));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    }

    private sealed class Entry(Brush baseBrush)
    {
        public Brush Base { get; set; } = baseBrush;
        public Brush? Applied { get; set; }
    }
}
