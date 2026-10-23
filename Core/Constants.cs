using System.IO;

namespace TrayClockApp.Core;

public static class Constants
{
    public const string EmojiFont =
        "Segoe UI Emoji, Segoe UI Symbol, Segoe UI, Noto Emoji, Noto Sans Symbols 2, Symbola, DejaVu Sans";

    public const int DefaultOpacity = 70;
    public const int DefaultComponentHeight = 25;
    public const bool DefaultDraggable = true;

    public const bool DefaultAntiBurnIn = true;
    public const bool DefaultAntiBurnInColor = true;
    public const int DefaultAntiBurnInAmplitudeN = 60;
    public const int DefaultAntiBurnInPeriodS = 10;
    public const int DefaultAntiBurnInColorPeriodS = 120;
    public const GradientMode DefaultAntiBurnInGradient = GradientMode.Linear;
    public const GradientMode DefaultAntiBurnInColorGradient = GradientMode.Linear;

    public const int MaxAmplitudeN = 100;
    public const int MinAntiBurnInPeriodS = 5;
    public const int MaxAntiBurnInPeriodS = 3600;

    public const long DataSize1Kb = 1024;
    public const long DataSize1Mb = 1024 * 1024;
    public const long DataSize1Gb = 1024 * 1024 * 1024;

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TrayClockApp");

    public static string ConfigFile => Path.Combine(ConfigDir, "config.json");
}