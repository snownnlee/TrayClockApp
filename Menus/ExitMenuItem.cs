using System.Windows.Forms;
using TrayClockApp.Core;

namespace TrayClockApp.Menus;

public class ExitMenuItem : MenuItemBase
{
    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var item = new ToolStripMenuItem("退出");
        item.Click += (_, _) => EventBus.Publish(EventType.Exit);
        menu.Items.Add(item);
    }
}