using System.Windows.Media;

namespace TrayClockApp.Core;

/// <summary>
/// sRGB 与 OKLab / OKLCH 的相互转换。
///
/// 用于缓变配色：OKLab 的明度 <c>L</c> 是感知均匀的，同一 <c>L</c> 在不同色相下的
/// 主观亮度基本一致；而 HSL 的 <c>L</c> 只是几何中点值，色相一变感知亮度就大幅起伏
/// （同为 L=0.5，黄色的 OKLab 明度约 0.97，蓝色仅约 0.45）。
/// 因此在 OKLCH 空间固定 L 旋转 H，才能让缓变过程中的文字始终清晰可读。
/// </summary>
public static class OkLch
{
    /// <summary>彩度低于此值即视为中性色（灰 / 白 / 黑），没有可旋转的色相</summary>
    public const double NeutralChroma = 0.02;

    /// <summary>把 sRGB 颜色分解为 OKLCH 的感知明度、彩度与色相角（0~360°）</summary>
    public static (double L, double C, double H) FromSrgb(Color color)
    {
        var r = ToLinearChannel(color.R);
        var g = ToLinearChannel(color.G);
        var b = ToLinearChannel(color.B);

        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        var okL = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        var okA = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        var okB = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;

        var chroma = Math.Sqrt(okA * okA + okB * okB);
        var hue = Math.Atan2(okB, okA) * 180 / Math.PI;
        if (hue < 0) hue += 360;

        return (okL, chroma, hue);
    }

    /// <summary>
    /// 由感知明度、彩度与色相角生成 sRGB 颜色。
    /// 当目标色彩度超出 sRGB 色域时，保持明度不变、二分压缩彩度，
    /// 即优先牺牲鲜艳度而不是可读性。
    /// </summary>
    public static Color ToSrgb(double l, double chroma, double hueDegrees)
    {
        var radians = hueDegrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var (r, g, b) = FromOklab(l, chroma * cos, chroma * sin);
        if (!InGamut(r) || !InGamut(g) || !InGamut(b))
        {
            double low = 0, high = chroma;
            for (var i = 0; i < 24; i++)
            {
                var mid = (low + high) / 2;
                (r, g, b) = FromOklab(l, mid * cos, mid * sin);
                if (InGamut(r) && InGamut(g) && InGamut(b)) low = mid;
                else high = mid;
            }

            (r, g, b) = FromOklab(l, low * cos, low * sin);
        }

        return Color.FromRgb(ToSrgbChannel(r), ToSrgbChannel(g), ToSrgbChannel(b));
    }

    private static (double R, double G, double B) FromOklab(double l, double a, double b)
    {
        var l_ = l + 0.3963377774 * a + 0.2158037573 * b;
        var m_ = l - 0.1055613458 * a - 0.0638541728 * b;
        var s_ = l - 0.0894841775 * a - 1.2914855480 * b;

        var l3 = l_ * l_ * l_;
        var m3 = m_ * m_ * m_;
        var s3 = s_ * s_ * s_;

        return (
            4.0767416621 * l3 - 3.3077115913 * m3 + 0.2309699292 * s3,
            -1.2684380046 * l3 + 2.6097574011 * m3 - 0.3413193965 * s3,
            -0.0041960863 * l3 - 0.7034186147 * m3 + 1.7076147010 * s3);
    }

    /// <summary>允许微小越界，避免二分在边界上反复抖动</summary>
    private static bool InGamut(double channel)
    {
        return channel >= -1e-4 && channel <= 1 + 1e-4;
    }

    private static double ToLinearChannel(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static byte ToSrgbChannel(double linear)
    {
        var value = Math.Clamp(linear, 0, 1);
        var encoded = value <= 0.0031308
            ? value * 12.92
            : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;

        return (byte)Math.Clamp(Math.Round(encoded * 255), 0, 255);
    }
}
