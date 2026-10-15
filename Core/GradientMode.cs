namespace TrayClockApp.Core;

/// <summary>缓变亮度的渐变模式：决定亮度随时间变化的曲线形状</summary>
public enum GradientMode
{
    /// <summary>正弦渐变：两端平缓、中间变化快，无明显折点</summary>
    Sine = 0,

    /// <summary>线性渐变：亮度匀速升降，两端存在折点</summary>
    Linear
}
