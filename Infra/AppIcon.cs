using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TrayClockApp.Infra;

public static class AppIcon
{
    private const string EmbeddedResourceName = "TrayClockApp.app.ico";

    private const string IconFileName = "app.ico";

    public static Icon LoadTrayIcon()
    {
        var size = SystemInformation.SmallIconSize;

        var externalPath = Path.Combine(AppContext.BaseDirectory, IconFileName);
        if (File.Exists(externalPath))
        {
            var external = TryLoad(() => new Icon(externalPath, size.Width, size.Height));
            if (external is not null) return external;
        }

        var embedded = TryLoad(() => LoadEmbedded(size));
        if (embedded is not null) return embedded;

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var fromProcess = TryLoad(() => Icon.ExtractAssociatedIcon(processPath));
            if (fromProcess is not null) return fromProcess;
        }

        Console.Error.WriteLine("app.ico 加载失败，回退到系统默认图标");
        return SystemIcons.Application;
    }

    private static Icon? LoadEmbedded(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is not null) return new Icon(stream, size.Width, size.Height);
        Console.Error.WriteLine($"未找到内嵌资源：{EmbeddedResourceName}");
        return null;
    }

    private static Icon? TryLoad(Func<Icon?> factory)
    {
        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"加载图标失败: {ex.Message}");
            return null;
        }
    }
}