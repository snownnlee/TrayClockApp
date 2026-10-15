using System.Windows;
using System.Windows.Threading;
using TrayClockApp.Core;

namespace TrayClockApp.Infra;

/// <summary>
/// 让窗口整体亮度在一定的降幅之内按所选渐变模式缓变（正弦或线性）。
/// 通过降低平均发光量来减缓 OLED 残像/烧屏。
/// </summary>
public sealed class BrightnessBreathing
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    private readonly Window _window;
    private readonly DispatcherTimer _timer;

    private double _phase;
    private int _amplitudeN;
    private int _periodS;
    private GradientMode _gradient = Constants.DefaultAntiBurnInGradient;

    public BrightnessBreathing(Window window)
    {
        _window = window;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Advance();
    }

    public void Configure(bool enabled, int amplitudeN, int periodS, GradientMode gradient)
    {
        _amplitudeN = Math.Clamp(amplitudeN, 0, Constants.MaxAmplitudeN);
        _periodS = Math.Clamp(periodS, Constants.MinAntiBurnInPeriodS, Constants.MaxAntiBurnInPeriodS);
        _gradient = gradient;

        _phase = 0;
        _window.Opacity = 1.0;

        if (!enabled || _amplitudeN == 0)
        {
            _timer.Stop();
            return;
        }

        if (!_timer.IsEnabled) _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _window.Opacity = 1.0;
    }

    private void Advance()
    {
        _phase += TickInterval.TotalSeconds / _periodS;
        if (_phase >= 1.0) _phase -= 1.0;

        var factor = _gradient == GradientMode.Linear ? LinearFactor(_phase) : SineFactor(_phase);
        _window.Opacity = 1.0 - _amplitudeN / 100.0 * factor;
    }

    /// <summary>正弦曲线：两端平缓、中间陡峭；相位 0 最亮，0.5 最暗</summary>
    private static double SineFactor(double phase)
    {
        return (1 - Math.Cos(2 * Math.PI * phase)) / 2;
    }

    /// <summary>三角波：亮度匀速升降，两端各有一个折点；相位 0 最亮，0.5 最暗</summary>
    private static double LinearFactor(double phase)
    {
        return phase < 0.5 ? phase * 2 : (1 - phase) * 2;
    }
}
