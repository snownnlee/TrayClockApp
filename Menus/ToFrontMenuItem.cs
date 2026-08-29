using System.Windows.Forms;
using TrayClockApp.Infra;

namespace TrayClockApp.Menus;

public class ToFrontMenuItem : MenuItemBase
{
    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var item = new ToolStripMenuItem("置顶");
        item.Click += async (_, _) =>
        {
            WindowUtil.ShowToast("执行中......");
            await Task.Delay(TimeSpan.FromMilliseconds(2100));
            WindowUtil.BringToFront(Window);
            WindowUtil.ShowToast("已执行置顶");
        };
        menu.Items.Add(item);
    }
}