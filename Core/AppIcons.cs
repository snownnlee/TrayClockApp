using System.Windows;
using System.Windows.Media;

namespace TrayClockApp.Core;

public static class AppIcons
{
    public static bool PlainText { get; private set; }

    private static readonly string[] EmojiFontCandidates = ["Segoe UI Emoji", "Noto Emoji"];

    public static void SetPlainText(bool plainText) => PlainText = plainText;

    public static void Initialize()
    {
        if (IsAnyFontAvailable(EmojiFontCandidates)) return;

        PlainText = true;
        Console.Error.WriteLine("未检测到可用的 emoji 字体，已自动启用纯文本图标模式");
    }

    private static bool IsAnyFontAvailable(string[] familyNames)
    {
        return familyNames.Select(familyName => new Typeface(new FontFamily(familyName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)).Any(typeface => typeface.TryGetGlyphTypeface(out _));
    }

    // ---------- 图标字符（emoji / 纯文本两套） ----------

    /// <summary>时钟。</summary>
    public static string Time => PlainText ? "[T] " : "\U0001F551";

    /// <summary>网络。</summary>
    public static string Network => PlainText ? "[N] " : "\U0001F310";

    /// <summary>内存。</summary>
    public static string Memory => PlainText ? "[M] " : "\U0001F4BE";

    /// <summary>电池。</summary>
    public static string Battery => PlainText ? "[B] " : "\U0001F50B";

    /// <summary>未检测到电池。</summary>
    public static string NoBattery => PlainText ? "[x]" : "\U0001F6AB";

    /// <summary>已接通交流电源。</summary>
    public static string PowerOnLine => PlainText ? "[AC]" : "\U0001F50C";

    /// <summary>电池正在充电。</summary>
    public static string Charging => PlainText ? "[CHG]" : "\u26A1";

    /// <summary>整机功耗。</summary>
    public static string Power => PlainText ? "[P] " : "\U0001F4A1";
}
