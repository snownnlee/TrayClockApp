using System.Windows.Forms;
using MenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus.MoreMenu;

public class KeepSystemRunMoreMenuItem : MoreMenuItemBase
{
    private const KeepAwakeMode TargetMode = KeepAwakeMode.SystemRun;

    public override bool AddUiMenuItem(MenuItem parentMenu)
    {
        var item = new MenuItem
        {
            Header = "保持系统运行",
            IsCheckable = true,
            IsChecked = KeepRunCoordinator.Mode == KeepAwakeMode.SystemRun,
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
        var item = new ToolStripMenuItem("保持系统运行")
        {
            Checked = KeepRunCoordinator.Mode == KeepAwakeMode.SystemRun,
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
        if (KeepRunCoordinator.Mode != KeepAwakeMode.SystemRun) return;
        KeepRunCoordinator.SetMode(KeepAwakeMode.None);
    }
}