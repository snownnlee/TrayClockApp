using System.IO;

namespace TrayClockApp.Core;

public static class Constants
{
    public const string EmojiFont = "Segoe UI Emoji";

    public const int DefaultOpacity = 30;
    public const int DefaultComponentHeight = 25;
    public const bool DefaultDraggable = true;

    public const long DataSize1Kb = 1024;
    public const long DataSize1Mb = 1024 * 1024;
    public const long DataSize1Gb = 1024 * 1024 * 1024;

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TrayClockApp");

    public static string ConfigFile => Path.Combine(ConfigDir, "config.json");
}