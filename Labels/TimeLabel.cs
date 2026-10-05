using System.Windows.Media;
using TrayClockApp.Core;

namespace TrayClockApp.Labels;

public class TimeLabel : LabelBase
{
    public override void Init()
    {
        base.Init();
        Text.Text = AppIcons.Time + "--:--:--";
        Text.Foreground = Brushes.Cyan;
    }

    public override void Update()
    {
        Text.Text = AppIcons.Time + DateTime.Now.ToString("HH:mm:ss");
    }
}
