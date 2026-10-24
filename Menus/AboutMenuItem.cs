using System.IO;
using System.Windows;
using System.Windows.Forms;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TrayClockApp.Menus;

/// <summary>
/// “关于”菜单：仅出现在托盘右键菜单中（不注入悬浮条右键菜单）。
/// 子项：显示当前版本、检查更新（检查 → 下载 → 备份替换 → 启动新版）。
/// </summary>
public class AboutMenuItem : MenuItemBase
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    private bool _busy;

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("关于");

        var versionItem = new ToolStripMenuItem($"当前版本：{AppVersion.Display}");
        versionItem.Click += (_, _) => ShowVersionInfo();
        aboutItem.DropDownItems.Add(versionItem);

        aboutItem.DropDownItems.Add(new ToolStripSeparator());

        var checkItem = new ToolStripMenuItem("检查更新");
        checkItem.Click += (_, _) =>
        {
            menu.Close();
            // 等托盘菜单先收起，再弹出等待对话框，避免菜单抢焦点
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) _ = RunCheckAsync();
            else dispatcher.BeginInvoke(new Action(() => _ = RunCheckAsync()));
        };
        aboutItem.DropDownItems.Add(checkItem);

        menu.Items.Add(aboutItem);
    }

    private void ShowVersionInfo()
    {
        var lines = new List<string>
        {
            $"{AppVersion.Product} {AppVersion.Display}"
        };

        if (!string.IsNullOrWhiteSpace(AppVersion.Company)) lines.Add(AppVersion.Company);

        lines.Add($"运行路径：{Environment.ProcessPath ?? "(未知)"}");

        if (!string.IsNullOrWhiteSpace(AppVersion.Copyright))
        {
            lines.Add(string.Empty);
            lines.Add(AppVersion.Copyright);
        }

        MessageBox.Show(string.Join(Environment.NewLine, lines), "关于",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private Task RunCheckAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.InvokeAsync(CheckFlowAsync).Task.Unwrap();
        return CheckFlowAsync();
    }

    private async Task CheckFlowAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var result = await QueryLatestAsync();
            if (result is null) return;

            switch (result.Status)
            {
                case UpdateCheckStatus.Failed:
                    MessageBox.Show(result.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;

                case UpdateCheckStatus.UpToDate:
                    MessageBox.Show(result.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;

                case UpdateCheckStatus.ManualOnly:
                    var open = MessageBox.Show(result.Message + "\n\n是否现在打开发布页？", "发现新版本",
                        MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (open == MessageBoxResult.Yes) UpdateService.OpenReleasesPage();
                    return;

                case UpdateCheckStatus.UpdateAvailable:
                    await ConfirmAndUpdateAsync(result);
                    return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("检查更新时发生未预期的错误：" + ex.Message, "检查更新",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task<UpdateCheckResult?> QueryLatestAsync()
    {
        var dialog = new UpdateDialog("检查更新", "正在检查最新版本，请稍候…");
        using var timeout = new CancellationTokenSource(CheckTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(dialog.Token, timeout.Token);

        dialog.Show();
        try
        {
            return await UpdateService.CheckAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            var message = dialog.Token.IsCancellationRequested
                ? "已取消检查更新。"
                : $"检查超时（{CheckTimeout.TotalSeconds:0} 秒内未取得响应），请检查网络后重试。";
            MessageBox.Show(message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        catch (Exception ex)
        {
            MessageBox.Show("检查更新失败：" + ex.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally
        {
            dialog.Complete();
        }
    }

    private async Task ConfirmAndUpdateAsync(UpdateCheckResult result)
    {
        var package = result.Package!;
        var answer = MessageBox.Show(
            $"发现新版本 {result.LatestTag}（当前 {AppVersion.Display}）。"
            + Environment.NewLine
            + Environment.NewLine
            + "更新将下载安装包到程序所在目录，把当前程序加版本号改名备份，然后启动新版本。"
            + Environment.NewLine
            + Environment.NewLine
            + "是否立即更新？",
            "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        await DownloadAndApplyAsync(package, result.LatestTag ?? AppVersion.Display);
    }

    private static async Task DownloadAndApplyAsync(UpdatePackage package, string latestTag)
    {
        var directory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(directory))
        {
            MessageBox.Show("无法定位程序所在目录，更新中止。", "更新失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new UpdateDialog("正在更新", $"正在下载 {latestTag}…");
        using var timeout = new CancellationTokenSource(DownloadTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(dialog.Token, timeout.Token);

        // 目标文件名由安装包名去掉版本号推导（TrayClockApp-v1.6.0.exe → TrayClockApp.exe）
        var targetExeName = UpdateService.GetTargetExeName(package);
        dialog.Show();

        string? downloadedPath = null;
        try
        {
            var progress = new Progress<DownloadProgress>(report => dialog.ReportProgress($"正在下载 {latestTag}…", report));
            downloadedPath = await UpdateService.DownloadAsync(package, directory, targetExeName, progress, linked.Token);

            dialog.SetIndeterminate("正在切换版本…", $"备份当前版本为 {Path.GetFileNameWithoutExtension(targetExeName)}-v{AppVersion.Current}.exe");
            var apply = UpdateService.ApplyAndRestart(downloadedPath, targetExeName);
            dialog.Complete();

            if (!apply.Success)
            {
                UpdateService.TryCleanup(downloadedPath);
                MessageBox.Show(apply.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            EventBus.Publish(EventType.Exit);
        }
        catch (OperationCanceledException)
        {
            dialog.Complete();
            UpdateService.TryCleanup(downloadedPath);
            var message = dialog.Token.IsCancellationRequested
                ? "已取消下载，程序保持当前版本。"
                : "下载超时，程序保持当前版本。";
            MessageBox.Show(message, "已取消更新", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            dialog.Complete();
            UpdateService.TryCleanup(downloadedPath);
            MessageBox.Show(ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
