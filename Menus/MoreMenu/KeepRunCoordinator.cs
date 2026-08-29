using System.Windows.Forms;
using TrayClockApp.Infra;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace TrayClockApp.Menus.MoreMenu;

internal static class KeepRunCoordinator
{
    public static readonly object GroupTag = new();

    public static KeepAwakeMode Mode { get; private set; } = KeepAwakeMode.None;

    public static void ToggleOption(bool on, KeepAwakeMode mode)
    {
        if (on)
        {
            SetMode(mode);
        }
        else if (Mode == mode)
        {
            SetMode(KeepAwakeMode.None);
        }
    }

    public static void SetMode(KeepAwakeMode mode)
    {
        var previous = Mode;
        if (previous == mode) return;
        Mode = mode;
        Apply(previous);
    }

    private static void Apply(KeepAwakeMode previous)
    {
        bool ok;
        string toast;
        switch (Mode)
        {
            case KeepAwakeMode.ScreenOn:
                ok = Win32.SetKeepScreenOn(true);
                toast = ok ? "设置【保持屏幕常亮】成功！" : "设置【保持屏幕常亮】失败！";
                break;
            case KeepAwakeMode.SystemRun:
                ok = Win32.SetKeepSystemRun(true);
                toast = ok ? "设置【保持系统运行】成功！" : "设置【保持系统运行】失败！";
                break;
            case KeepAwakeMode.None:
            default:
                ok = Win32.SetKeepScreenOn(false);
                toast = previous == KeepAwakeMode.ScreenOn
                    ? ok ? "取消【保持屏幕常亮】成功！" : "取消【保持屏幕常亮】失败！"
                    : ok
                        ? "取消【保持系统运行】成功！"
                        : "取消【保持系统运行】失败！";
                break;
        }

        WindowUtil.ShowToast(toast);
    }

    public static void UncheckOtherWpfItems(WpfMenuItem parentMenu, WpfMenuItem self)
    {
        foreach (var obj in parentMenu.Items)
            if (obj is WpfMenuItem mi && mi != self && ReferenceEquals(mi.Tag, GroupTag))
                mi.IsChecked = false;
    }

    public static void UncheckOtherTrayItems(ToolStripMenuItem parentMenu, ToolStripMenuItem self)
    {
        foreach (var obj in parentMenu.DropDownItems)
            if (obj is ToolStripMenuItem mi && mi != self && ReferenceEquals(mi.Tag, GroupTag))
                mi.Checked = false;
    }
}