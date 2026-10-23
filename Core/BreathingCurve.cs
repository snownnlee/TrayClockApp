namespace TrayClockApp.Core;

/// <summary>缓变类功能共用的曲线：把 0~1 的相位映射为 0~1 的变化量。</summary>
public static class BreathingCurve
{
    public static double Factor(GradientMode mode, double phase)
    {
        return mode == GradientMode.Linear ? Linear(phase) : Sine(phase);
    }

    /// <summary>正弦曲线：两端平缓、中间陡峭；相位 0 变化量为 0，0.5 为 1</summary>
    private static double Sine(double phase)
    {
        return (1 - Math.Cos(2 * Math.PI * phase)) / 2;
    }

    /// <summary>三角波：变化量匀速升降，两端各有一个折点；相位 0 变化量为 0，0.5 为 1</summary>
    private static double Linear(double phase)
    {
        return phase < 0.5 ? phase * 2 : (1 - phase) * 2;
    }
}
