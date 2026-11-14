namespace TrayClockApp.Core;

/// <summary>悬浮条边框样式</summary>
public enum BorderMode
{
    /// <summary>无边框（透明占位，尺寸与其他模式一致）</summary>
    None = 0,

    /// <summary>普通边框：1 像素单色</summary>
    Normal,

    /// <summary>彩色边框：1 像素彩虹渐变并流动</summary>
    Colorful
}
