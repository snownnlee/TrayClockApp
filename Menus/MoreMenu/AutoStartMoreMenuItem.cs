using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using TrayClockApp.Infra;
using Application = System.Windows.Application;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;

namespace TrayClockApp.Menus.MoreMenu;

/// <summary>
/// “注册开机启动”：勾选状态以计划任务“存在且已启用”为准（指向其他路径时在标题中提示），
/// 点击先弹窗确认，确认后注册/取消，完成后提示结果。
/// </summary>
public class AutoStartMoreMenuItem : MoreMenuItemBase
{
    private const string Title = "注册开机启动";
    private const string ElsewhereHint = "（已指向其它路径）";

    public override bool AddUiMenuItem(MenuItem parentMenu)
    {
        var status = AutoStartService.GetStatus();
        var item = new MenuItem
        {
            Header = BuildHeader(status),
            IsCheckable = true,
            IsChecked = IsRegistered(status)
        };
        item.Click += (_, _) => ToggleFromUi(item);
        parentMenu.Items.Add(item);
        return true;
    }

    public override void AddTrayMenuItem(ToolStripMenuItem parentMenu)
    {
        var status = AutoStartService.GetStatus();
        var item = new ToolStripMenuItem(BuildHeader(status))
        {
            Checked = IsRegistered(status),
            CheckOnClick = false
        };
        item.Click += (_, _) => ToggleFromTray(item);
        parentMenu.DropDownItems.Add(item);
    }

    private static bool IsRegistered(AutoStartStatus status) => status != AutoStartStatus.NotRegistered;

    private static string BuildHeader(AutoStartStatus status) =>
        status == AutoStartStatus.PointsElsewhere ? Title + ElsewhereHint : Title;

    private static void ToggleFromUi(MenuItem item)
    {
        // 可勾选菜单项在 Click 前已被 WPF 翻转，这里按计划任务的真实状态还原
        var status = AutoStartService.GetStatus();
        var registered = IsRegistered(status);
        item.IsChecked = registered;
        item.Header = BuildHeader(status);
        DeferToggle(registered, applyState: isRegistered =>
        {
            item.IsChecked = isRegistered;
            item.Header = BuildHeader(isRegistered ? AutoStartStatus.Registered : AutoStartStatus.NotRegistered);
        });
    }

    private static void ToggleFromTray(ToolStripMenuItem item)
    {
        var status = AutoStartService.GetStatus();
        var registered = IsRegistered(status);
        item.Checked = registered;
        item.Text = BuildHeader(status);
        DeferToggle(registered, applyState: isRegistered =>
        {
            item.Checked = isRegistered;
            item.Text = BuildHeader(isRegistered ? AutoStartStatus.Registered : AutoStartStatus.NotRegistered);
        });
    }

    /// <summary>等菜单收起后再弹确认框，避免菜单抢焦点。</summary>
    private static void DeferToggle(bool registered, Action<bool> applyState)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) _ = ToggleAsync(registered, applyState);
        else dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = ToggleAsync(registered, applyState)));
    }

    private static async Task ToggleAsync(bool registered, Action<bool> applyState)
    {
        var target = !registered;
        var message = target
            ? $"确定要注册开机启动吗？{Environment.NewLine}{Environment.NewLine}"
              + $"将创建（或覆盖）计划任务「{AutoStartService.TaskName}」，使其指向当前程序并在登录 Windows 后自动启动。"
              + $"{Environment.NewLine}{Environment.NewLine}该操作通常需要管理员权限，可能会弹出 UAC 授权提示。"
            : $"确定要取消开机启动吗？{Environment.NewLine}{Environment.NewLine}"
              + $"将删除计划任务「{AutoStartService.TaskName}」，登录 Windows 后不再自动启动本程序。"
              + $"{Environment.NewLine}{Environment.NewLine}该操作通常需要管理员权限，可能会弹出 UAC 授权提示。";

        var answer = MessageBox.Show(message, Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var result = await AutoStartService.SetEnabledAsync(target);

        if (result.Cancelled) return;

        if (result.Success)
        {
            applyState(target);
            WindowUtil.ShowToast(result.Message);
            return;
        }

        MessageBox.Show(result.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
