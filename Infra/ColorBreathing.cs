using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TrayClockApp.Core;

namespace TrayClockApp.Infra;

/// <summary>
/// 让各标签文字的色相沿色相环缓变，通过让像素点在色相环上移动，
/// 避免同一位置的子像素长期承受相同的发光衰减。
///
/// 色相旋转在 OKLCH 空间进行并固定感知明度：HSL 的 L 并不代表视觉亮度，
/// 直接沿用会让文字转到蓝色段时与背景同亮度而不可读（实测对比度低至 1.18:1）。
/// 统一明度后整圈亮度比约 1.14×，最暗处仍有 6:1 以上的对比度。
/// </summary>
public sealed class ColorBreathing
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>一个周期内的色相摆动总角度</summary>
    private const double HueSweepDegrees = 360.0;

    /// <summary>缓变时统一的感知明度（OKLab L）</summary>
    private const double TargetLightness = 0.85;

    /// <summary>目标彩度（OKLCH C）；超出 sRGB 色域时自动降低，优先保明度</summary>
    private const double TargetChroma = 0.19;

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
            SetBase(entry, text.Foreground);
        }
        else if (!ReferenceEquals(text.Foreground, entry.Applied))
        {
            SetBase(entry, text.Foreground);
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

        SetBase(entry, text.Foreground);
        ApplyShift(text, entry);
    }

    /// <summary>记录标签的原始色，并解析出它在 OKLCH 中的色相基准</summary>
    private static void SetBase(Entry entry, Brush brush)
    {
        entry.Base = brush;

        if (brush is SolidColorBrush solid)
        {
            var (_, chroma, hue) = OkLch.FromSrgb(solid.Color);
            entry.Hue = hue;
            entry.Neutral = chroma < OkLch.NeutralChroma;
        }
        else
        {
            entry.Neutral = true;
        }
    }

    private void ApplyShift(TextBlock text, Entry entry)
    {
        // 中性色（灰 / 白 / 黑）没有色相可旋转，强行上色会破坏"灰色 = 无电池"之类的语义，
        // 因此保持原色，仅记录引用以免下一帧重复解析。
        if (entry.Neutral)
        {
            entry.Applied = text.Foreground;
            return;
        }

        var alpha = entry.Base is SolidColorBrush brush ? brush.Color.A : (byte)255;
        var color = OkLch.ToSrgb(TargetLightness, TargetChroma, entry.Hue + _hueShift);

        var applied = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        applied.Freeze();
        entry.Applied = applied;

        _applying = true;
        text.Foreground = applied;
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

    private sealed class Entry(Brush baseBrush)
    {
        public Brush Base { get; set; } = baseBrush;
        public Brush? Applied { get; set; }

        /// <summary>基础色在 OKLCH 中的色相角（度）</summary>
        public double Hue { get; set; }

        /// <summary>基础色接近无色（灰 / 白 / 黑），不参与色相旋转</summary>
        public bool Neutral { get; set; }
    }
}
