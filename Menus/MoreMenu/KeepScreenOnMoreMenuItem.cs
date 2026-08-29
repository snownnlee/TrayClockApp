using System.Windows.Forms;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus.MoreMenu;

public class KeepScreenOnMoreMenuItem : MoreMenuItemBase
{
    private const KeepAwakeMode TargetMode = KeepAwakeMode.ScreenOn;

    public override bool AddUiMenuItem(MenuItem parentMenu)
    {
        var item = new MenuItem
        {
            Header = "保持屏幕常亮",
            IsCheckable = true,
            IsChecked = KeepRunCoordinator.Mode == KeepAwakeMode.ScreenOn,
            Tag = KeepRunCoordinator.GroupTag
        };
        item.Click += (_, _) =>
        {
            KeepRunCoordinator.ToggleOption(item.IsChecked, TargetMode);
            if (item.IsChecked) KeepRunCoordinator.UncheckOtherWpfItems(parentMenu, item);
        };
        parentMenu.Items.Add(item);
        return true;
    }

    public override void AddTrayMenuItem(ToolStripMenuItem parentMenu)
    {
        var item = new ToolStripMenuItem("保持屏幕常亮")
        {
            Checked = KeepRunCoordinator.Mode == KeepAwakeMode.ScreenOn,
            CheckOnClick = true,
            Tag = KeepRunCoordinator.GroupTag
        };
        item.CheckedChanged += (_, _) =>
        {
            KeepRunCoordinator.ToggleOption(item.Checked, TargetMode);
            if (item.Checked) KeepRunCoordinator.UncheckOtherTrayItems(parentMenu, item);
        };
        parentMenu.DropDownItems.Add(item);
    }

    public override void OnStop()
    {
        if (KeepRunCoordinator.Mode != KeepAwakeMode.ScreenOn) return;
        KeepRunCoordinator.SetMode(KeepAwakeMode.None);
    }
}
