using System.Windows.Media;

namespace TrayClockApp.Labels;

public class TimeLabel : LabelBase
{
    private const string Emoji = "\U0001F551";

    public override void Init()
    {
        base.Init();
        Text.Text = Emoji + "--:--:--";
        Text.Foreground = Brushes.Cyan;
    }

    public override void Update()
    {
        Text.Text = Emoji + DateTime.Now.ToString("HH:mm:ss");
    }
}