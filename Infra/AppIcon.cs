using System.Drawing;
using System.Drawing.Drawing2D;

namespace TrayClockApp.Infra;

public static class AppIcon
{
    public static Icon DrawTrayIcon()
    {
        const int size = 32;
        const double s = 32.0 / 128.0;

        var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            const double centerX = 64 * s;
            const double centerY = 64 * s;
            const float stroke = (float)(13 * s);

            const double screenWidth = 110 * s;
            const double screenHeight = 80 * s;
            const double screenX = centerX - screenWidth / 2;
            const double screenY = centerY - screenHeight / 2 - 9 * s;

            using (var whitePen = new Pen(Color.White, stroke))
            {
                whitePen.LineJoin = LineJoin.Round;
                whitePen.StartCap = LineCap.Round;
                whitePen.EndCap = LineCap.Round;
                g.DrawPath(whitePen, RoundRect((float)screenX, (float)screenY, (float)screenWidth, (float)screenHeight, (float)(12 * s)));
            }

            const double innerPad = 16 * s;
            var p1 = new PointF((float)(screenX + innerPad), (float)(screenY + screenHeight - innerPad - 4 * s));
            var p2 = new PointF((float)(centerX - 16 * s), (float)(screenY + innerPad + 2 * s));
            var p3 = new PointF((float)(centerX + 16 * s), (float)(screenY + screenHeight - innerPad - 2 * s));
            var p4 = new PointF((float)(screenX + screenWidth - innerPad), (float)(screenY + innerPad + 2 * s));

            using (var greenPen = new Pen(Color.Lime, stroke))
            {
                greenPen.LineJoin = LineJoin.Round;
                greenPen.StartCap = LineCap.Round;
                greenPen.EndCap = LineCap.Round;
                g.DrawLines(greenPen, new[] { p1, p2, p3, p4 });
            }

            g.FillEllipse(new SolidBrush(Color.FromArgb(240, 255, 255, 255)),
                p4.X - 5 * (float)s, p4.Y - 5 * (float)s,
                10 * (float)s, 10 * (float)s);

            const double baseWidth = 50 * s;
            const double baseY = screenY + screenHeight + 24 * s;
            using (var whitePen = new Pen(Color.White, stroke))
            {
                whitePen.LineJoin = LineJoin.Round;
                whitePen.StartCap = LineCap.Round;
                whitePen.EndCap = LineCap.Round;
                g.DrawLine(whitePen,
                    new PointF((float)(centerX - baseWidth / 2), (float)baseY),
                    new PointF((float)(centerX + baseWidth / 2), (float)baseY));
            }
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            Win32.DestroyIconHandle(hIcon);
            bitmap.Dispose();
        }
    }

    private static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var d = r * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}