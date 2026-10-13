using System.Windows;
using System.Windows.Threading;
using TrayClockApp.Core;

namespace TrayClockApp.Infra;

/// <summary>
/// 让窗口整体亮度在一定的降幅之内余弦缓变。
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

    public BrightnessBreathing(Window window)
    {
        _window = window;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Advance();
    }

    public void Configure(bool enabled, int amplitudeN, int periodS)
    {
        _amplitudeN = Math.Clamp(amplitudeN, 0, Constants.MaxAmplitudeN);
        _periodS = Math.Clamp(periodS, Constants.MinAntiBurnInPeriodS, Constants.MaxAntiBurnInPeriodS);

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

        var factor = (1 - Math.Cos(2 * Math.PI * _phase)) / 2;
        _window.Opacity = 1.0 - _amplitudeN / 100.0 * factor;
    }
}
