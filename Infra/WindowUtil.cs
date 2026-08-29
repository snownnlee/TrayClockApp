using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DispatcherTimer = System.Windows.Threading.DispatcherTimer;

namespace TrayClockApp.Infra;

public static class WindowUtil
{
    public static void ShowErrorDialog(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void ShowToast(string message)
    {
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

            var workArea = SystemParameters.WorkArea;
            toast.Left = (workArea.Width - toast.ActualWidth) / 2;
            toast.Top = workArea.Height * 2 / 3;
            toast.Activate();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                toast.Close();
            };
            timer.Start();
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
}