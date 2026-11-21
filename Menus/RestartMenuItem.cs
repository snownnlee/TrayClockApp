using System.Security.Principal;
using System.Windows;
using System.Windows.Forms;
using TrayClockApp.Core;
using MessageBox = System.Windows.MessageBox;

namespace TrayClockApp.Menus;

/// <summary>重启菜单：普通重启 / 以管理员身份重启（后者会弹 UAC 授权）。
/// 当前已是管理员权限时无需提权，直接给出单个“重启”项，不再带子菜单。</summary>
public class RestartMenuItem : MenuItemBase
{
    private const string HeaderText = "重启";
    private const string RestartText = "重启";
    private const string RestartAsAdminText = "以管理员身份重启";
    private const string ConfirmTitle = "请确认";

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        // 已经是管理员权限：提权重启没有意义，直接一个“重启”项
        if (IsAdministrator)
        {
            var singleItem = new ToolStripMenuItem(RestartText);
            singleItem.Click += (_, _) => ConfirmAndPublish(EventType.Restart);
            menu.Items.Add(singleItem);
            return;
        }

        var restartMenu = new ToolStripMenuItem(HeaderText);

        var restartItem = new ToolStripMenuItem(RestartText);
        restartItem.Click += (_, _) => ConfirmAndPublish(EventType.Restart);
        restartMenu.DropDownItems.Add(restartItem);

        var adminItem = new ToolStripMenuItem(RestartAsAdminText);
        adminItem.Click += (_, _) => ConfirmAndPublish(EventType.RestartAsAdmin);
        restartMenu.DropDownItems.Add(adminItem);

        menu.Items.Add(restartMenu);
    }

    /// <summary>当前进程是否以管理员权限运行。</summary>
    private static bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    private static void ConfirmAndPublish(EventType eventType)
    {
        var message = eventType == EventType.RestartAsAdmin
            ? $"确定要以管理员身份重启吗？{Environment.NewLine}{Environment.NewLine}"
              + "将弹出 UAC 授权提示，确认后本程序以管理员权限重新启动；取消则保持当前运行状态。"
            : $"确定要重启吗？{Environment.NewLine}{Environment.NewLine}"
              + "将先保存配置，再以当前权限重新启动本程序。";

        var result = MessageBox.Show(message, ConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) EventBus.Publish(eventType);
    }
}
