using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DispatcherTimer = System.Windows.Threading.DispatcherTimer;

namespace TrayClockApp.Infra;

public static class WindowUtil
{
    private static nint _topmostHwnd;

    public static void ShowErrorDialog(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void ShowToast(string message) => ShowToast(message, TimeSpan.FromSeconds(2));

    public static void ShowToast(string message, TimeSpan duration)
        => ShowToast(message, duration, showCountdown: false);

    public static void ShowToast(string message, TimeSpan duration, bool showCountdown)
    {
        if (duration <= TimeSpan.Zero) duration = TimeSpan.FromSeconds(2);

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var toast = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Topmost = true,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                Focusable = false,
                SizeToContent = SizeToContent.WidthAndHeight
            };
            var label = new TextBlock
            {
                Text = message,
                FontFamily = new FontFamily("微软雅黑"),
                FontSize = 14,
                Foreground = Brushes.Lime,
                Margin = new Thickness(25, 12, 25, 12)
            };
            toast.Content = label;
            toast.Show();
            toast.UpdateLayout();

            var workArea = MonitorUtil.GetCursorWorkArea();
            toast.Left = workArea.Left + (workArea.Width - toast.ActualWidth) / 2;
            toast.Top = workArea.Top + workArea.Height * 2 / 3;
            toast.Activate();

            var remaining = duration;
            if (showCountdown) UpdateText();

            var timer = new DispatcherTimer { Interval = showCountdown ? TimeSpan.FromSeconds(1) : duration };
            timer.Tick += (_, _) =>
            {
                if (showCountdown)
                {
                    remaining -= TimeSpan.FromSeconds(1);
                    if (remaining > TimeSpan.Zero)
                    {
                        UpdateText();
                        return;
                    }
                }

                timer.Stop();
                toast.Close();
            };
            timer.Start();
            return;

            void UpdateText()
            {
                label.Text = $"{message}（{Math.Ceiling(remaining.TotalSeconds)}s）";
                toast.Topmost = false;
                toast.Topmost = true;
                toast.UpdateLayout();
                toast.Left = workArea.Left + (workArea.Width - toast.ActualWidth) / 2;
            }
        });
    }

    public static void ToggleVisibility(Window window)
    {
        window.Visibility = window.IsVisible ? Visibility.Hidden : Visibility.Visible;
    }

    public static void BringToFront(Window? window)
    {
        if (window is null) return;
        if (!window.IsVisible) window.Show();
        window.Activate();
        window.Topmost = false;
        window.Topmost = true;
    }

    public static void KeepOnTop(Window? window)
    {
        if (window is null || !window.IsVisible) return;

        if (_topmostHwnd == nint.Zero) _topmostHwnd = new WindowInteropHelper(window).Handle;
        if (_topmostHwnd == nint.Zero) return;

        if (Win32.IsTopOfZOrder(_topmostHwnd)) return;
        Win32.PinToTop(_topmostHwnd);
    }

    public static void ResetTopmostCache() => _topmostHwnd = nint.Zero;
}