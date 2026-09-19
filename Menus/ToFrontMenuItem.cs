using System.Windows.Forms;
using TrayClockApp.Infra;

namespace TrayClockApp.Menus;

public class ToFrontMenuItem : MenuItemBase
{
    private static readonly (string Text, int DelayMs)[] DelayList =
    [
        ("立即", 0),
        ("1秒后", 1000),
        ("3秒后", 3000),
        ("5秒后", 5000),
        ("7秒后", 7000),
        ("9秒后", 9000)
    ];

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var item = new ToolStripMenuItem("置顶");
        item.MouseUp += async (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            menu.Close();
            await BringToFrontAsync(0);
        };
        foreach (var (text, delayMs) in DelayList)
        {
            var subItem = new ToolStripMenuItem(text);
            subItem.Click += async (_, _) => await BringToFrontAsync(delayMs);
            item.DropDownItems.Add(subItem);
        }

        menu.Items.Add(item);
    }

    private async Task BringToFrontAsync(int delayMs)
    {
        if (delayMs > 0)
        {
            WindowUtil.ShowToast($"{delayMs / 1000.0:0.#}秒后执行置顶", TimeSpan.FromMilliseconds(delayMs), true);
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
        }

        WindowUtil.BringToFront(Window);
        WindowUtil.ShowToast("已执行置顶");
    }
}