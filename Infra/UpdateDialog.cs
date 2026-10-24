using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TrayClockApp.Infra;

/// <summary>
/// 检查/下载期间显示的等待对话框。采用系统标准窗体外壳（带标题栏、可移动、居中），
/// 进度条在不确定态与确定态之间切换，并提供“取消”按钮。
/// 必须从 UI 线程创建与调用。
/// </summary>
public sealed class UpdateDialog : Window
{
    private readonly TextBlock _messageText;
    private readonly TextBlock _detailText;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancelButton;
    private readonly CancellationTokenSource _cts = new();
    private bool _allowClose;

    public UpdateDialog(string title, string message)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.SingleBorderWindow;
        FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
        FontSize = 12.5;

        _messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        _progressBar = new ProgressBar
        {
            Height = 12,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 14, 0, 0),
            IsIndeterminate = true
        };

        _detailText = new TextBlock
        {
            Text = string.Empty,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        };

        _cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 78,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _cancelButton.Click += (_, _) => RequestCancel();

        var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        panel.Children.Add(_messageText);
        panel.Children.Add(_progressBar);
        panel.Children.Add(_detailText);
        panel.Children.Add(_cancelButton);
        Content = panel;

        // 检查/下载期间不允许通过标题栏关闭按钮或 Alt+F4 直接消失
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            RequestCancel();
        };
    }

    public CancellationToken Token => _cts.Token;

    public void Report(string message, string? detail = null)
    {
        RunOnUi(() =>
        {
            _messageText.Text = message;
            if (detail is not null) _detailText.Text = detail;
        });
    }

    public void ReportProgress(string message, DownloadProgress progress)
    {
        RunOnUi(() =>
        {
            _messageText.Text = message;
            _progressBar.IsIndeterminate = false;
            _progressBar.Value = progress.Total > 0 ? progress.Percent : 0;

            var receivedText = FormatBytes(progress.Received);
            _detailText.Text = progress.Total > 0
                ? $"{receivedText} / {FormatBytes(progress.Total)}（{progress.Percent:0}%）"
                : $"已下载 {receivedText}";
        });
    }

    public void SetIndeterminate(string message, string? detail = null)
    {
        RunOnUi(() =>
        {
            _messageText.Text = message;
            _progressBar.IsIndeterminate = true;
            if (detail is not null) _detailText.Text = detail;
        });
    }

    /// <summary>正常结束：允许关闭并立即关闭。</summary>
    public void Complete()
    {
        _allowClose = true;
        RunOnUi(Close);
    }

    private void RequestCancel()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        _cancelButton.IsEnabled = false;
        _cancelButton.Content = "正在取消…";
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action, DispatcherPriority.Normal);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => (bytes / 1024.0 / 1024 / 1024).ToString("0.00", CultureInfo.InvariantCulture) + " GB",
        >= 1024L * 1024 => (bytes / 1024.0 / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        >= 1024L => (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => bytes + " B"
    };
}
