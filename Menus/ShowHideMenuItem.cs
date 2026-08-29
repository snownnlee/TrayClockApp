using System.Windows.Forms;
using TrayClockApp.Infra;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus;

public class ShowHideMenuItem : MenuItemBase
{
    private const string Hide = "隐藏";
    private const string Show = "显示";

    public override void AddUiMenuItem(ContextMenu menu)
    {
        var item = new MenuItem { Header = Window!.IsVisible ? Hide : Show };
        item.Click += (_, _) => ToggleAndToast();
        menu.Items.Add(item);
    }

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        var item = new ToolStripMenuItem(Window!.IsVisible ? Hide : Show);
        item.Click += (_, _) =>
        {
            ToggleAndToast();
            item.Text = Window.IsVisible ? Hide : Show;
        };
        menu.Items.Add(item);
    }

    private void ToggleAndToast()
    {
        WindowUtil.ToggleVisibility(Window!);
        WindowUtil.ShowToast(Window!.IsVisible ? "已显示" : "已隐藏");
    }
}