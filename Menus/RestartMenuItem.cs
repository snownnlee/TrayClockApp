using System.Windows;
using System.Windows.Forms;
using TrayClockApp.Core;
using MessageBox = System.Windows.MessageBox;

namespace TrayClockApp.Menus;

public class RestartMenuItem : MenuItemBase
{
    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var item = new ToolStripMenuItem("重启");
        item.Click += (_, _) =>
        {
            var result = MessageBox.Show("确定要重启吗？", "请确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes) EventBus.Publish(EventType.Restart);
        };
        menu.Items.Add(item);
    }
}