using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Screen = System.Windows.Forms.Screen;

namespace TrayClockApp.Infra;

public static class MonitorUtil
{
    private static double DipScale
    {
        get
        {
            var primary = Screen.PrimaryScreen;
            var primaryDipWidth = SystemParameters.PrimaryScreenWidth;
            if (primary is null || primary.Bounds.Width <= 0 || primaryDipWidth <= 0) return 1.0;
            return primaryDipWidth / primary.Bounds.Width;
        }
    }

    private static Rect ToDipRect(Rectangle physical)
    {
        var s = DipScale;
        return new Rect(physical.X * s, physical.Y * s, physical.Width * s, physical.Height * s);
    }

    private static Rect WorkAreaDip(Screen screen) => ToDipRect(screen.WorkingArea);

    public static IEnumerable<Rect> GetAllWorkAreas() => Screen.AllScreens.Select(WorkAreaDip);

    public static Rect GetCursorWorkArea() => WorkAreaDip(Screen.FromPoint(Control.MousePosition));
}
