using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using TrayClockApp.Labels;
using TrayClockApp.Menus;

namespace TrayClockApp;

public partial class MainWindow
{
    private const int GwlExStyle = -20;
    private const int WsExToolwindow = 0x00000080;

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetWindowLongW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial void SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    private double? _configX;
    private double? _configY;
    private Point _dragStart;
    private bool _draggable = Constants.DefaultDraggable;
    private bool _dragged;

    private BorderMode _borderMode = Constants.DefaultBorderMode;
    private byte _backgroundAlpha = 0x7F; // 底色 alpha，由不透明度菜单设置（默认 50%）
    private readonly List<IMenuItem> _menuItems;
    private readonly BrightnessBreathing _brightness;
    private readonly ColorBreathing _color;

    public MainWindow(IEnumerable<ILabel> labels, List<IMenuItem> menuItems)
    {
        InitializeComponent();

        _menuItems = menuItems;
        _brightness = new BrightnessBreathing(this);
        _color = new ColorBreathing(LabelHost);

        foreach (var label in labels) LabelHost.Children.Add(label.GetTextBlock());

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _brightness.Stop();
            _color.Stop();
        };
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += OnMouseRightButtonUp;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLongW(hwnd, GwlExStyle, GetWindowLongW(hwnd, GwlExStyle) | WsExToolwindow);
    }

    public void SetDraggable(bool draggable)
    {
        _draggable = draggable;
    }

    /// <summary>边框色环（红 → 橙 → 黄 → 绿 → 青 → 蓝 → 紫 → 粉），首尾相近以便无缝循环。</summary>
    private static readonly Color[] BorderRainbow =
    [
        Color.FromRgb(0xFF, 0x3B, 0x30),
        Color.FromRgb(0xFF, 0x95, 0x00),
        Color.FromRgb(0xFF, 0xD6, 0x00),
        Color.FromRgb(0x34, 0xC7, 0x59),
        Color.FromRgb(0x00, 0xC7, 0xD9),
        Color.FromRgb(0x00, 0x7A, 0xFF),
        Color.FromRgb(0xAF, 0x52, 0xDE),
        Color.FromRgb(0xFF, 0x2D, 0x95)
    ];

    private const double BorderFlowSeconds = 6; // 色环走完一圈的时长
    private const int BorderFlowFrameRate = 30; // 细线不需要 60fps，降帧省渲染

    /// <summary>设置底色与边框的不透明度（0~255，两者共用）。</summary>
    public void SetBackgroundAlpha(byte alpha)
    {
        _backgroundAlpha = alpha;
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        ApplyBorderStyle();
    }

    /// <summary>切换边框样式。</summary>
    public void SetBorderMode(BorderMode mode)
    {
        _borderMode = mode;
        ApplyBorderStyle();
    }

    /// <summary>按当前样式与 alpha 重建边框。</summary>
    private void ApplyBorderStyle()
    {
        switch (_borderMode)
        {
            case BorderMode.None:
                // 透明画刷占位，保证三种模式尺寸一致
                RootBorder.BorderThickness = new Thickness(1);
                RootBorder.BorderBrush = Brushes.Transparent;
                break;

            case BorderMode.Normal:
                RootBorder.BorderThickness = new Thickness(1);
                RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(_backgroundAlpha, 0xFF, 0xFF, 0xFF));
                break;

            case BorderMode.Colorful:
            default:
                RootBorder.BorderThickness = new Thickness(1);
                RootBorder.BorderBrush = CreateFlowingRainbowBrush(_backgroundAlpha);
                break;
        }
    }

    /// <summary>铺两个色环并横向平移一个色环的宽度，循环处图案重合、无接缝。</summary>
    private static LinearGradientBrush CreateFlowingRainbowBrush(byte alpha)
    {
        const int cycles = 2;

        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        var steps = BorderRainbow.Length * cycles;
        for (var i = 0; i <= steps; i++)
        {
            var color = BorderRainbow[i % BorderRainbow.Length];
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(alpha, color.R, color.G, color.B),
                i / (double)steps));
        }

        var flow = new TranslateTransform();
        brush.RelativeTransform = flow;

        var animation = new DoubleAnimation(0, -1.0 / cycles, TimeSpan.FromSeconds(BorderFlowSeconds))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        Timeline.SetDesiredFrameRate(animation, BorderFlowFrameRate);

        flow.BeginAnimation(TranslateTransform.XProperty, animation);

        return brush;
    }

    public void SetPositionFromConfig(double x, double y)
    {
        _configX = x;
        _configY = y;
    }

    public void SetBrightnessBreathing(bool enabled, int amplitudeN, int periodS, GradientMode gradient)
    {
        _brightness.Configure(enabled, amplitudeN, periodS, gradient);
    }

    public void SetColorBreathing(bool enabled, int periodS, GradientMode gradient)
    {
        _color.Configure(enabled, periodS, gradient);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_configX is { } savedX && _configY is { } savedY && IsFullyInsideSomeWorkArea(savedX, savedY))
        {
            Left = savedX;
            Top = savedY;
            return;
        }

        MoveToCursorScreen();
    }

    private bool IsFullyInsideSomeWorkArea(double x, double y)
    {
        return MonitorUtil.GetAllWorkAreas().Any(area =>
            x >= area.Left
            && y >= area.Top
            && x + ActualWidth <= area.Right
            && y + ActualHeight <= area.Bottom
        );
    }

    public void MoveToCursorScreen()
    {
        var area = MonitorUtil.GetCursorWorkArea();
        Left = area.Left + Math.Max(0, area.Width - ActualWidth) / 3;
        Top = area.Top;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_draggable) return;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggable || !IsMouseCaptured) return;
        var delta = e.GetPosition(this) - _dragStart;
        if (delta.X != 0 || delta.Y != 0) _dragged = true;

        var area = MonitorUtil.GetCursorWorkArea();
        var maxX = Math.Max(area.Left, area.Right - ActualWidth);
        var maxY = Math.Max(area.Top, area.Bottom - ActualHeight);
        Left = Math.Clamp(Left + delta.X, area.Left, maxX);
        Top = Math.Clamp(Top + delta.Y, area.Top, maxY);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        if (!_dragged) return;
        _dragged = false;
        EventBus.Publish(EventType.ConfigChanged);
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };
        foreach (var menuItem in _menuItems) menuItem.AddUiMenuItem(menu);
        FixSubmenuHoverGap(menu);
        menu.IsOpen = true;
    }

    private static IEnumerable<MenuItem> EnumerateMenuItems(ItemCollection items)
    {
        foreach (var obj in items)
            if (obj is MenuItem mi)
            {
                yield return mi;
                foreach (var sub in EnumerateMenuItems(mi.Items)) yield return sub;
            }
    }

    private static void FixSubmenuHoverGap(ContextMenu menu)
    {
        foreach (var mi in EnumerateMenuItems(menu.Items))
        {
            if (!mi.HasItems) continue;
            mi.SubmenuOpened += (_, _) =>
            {
                if (mi.Template?.FindName("PART_Popup", mi) is not Popup popup) return;
                popup.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => AdjustSubmenuOverlap(popup)));
            };
        }
    }

    private static void AdjustSubmenuOverlap(Popup popup)
    {
        if (popup.Child is null) return;

        popup.HorizontalOffset = 0;
        popup.VerticalOffset = 0;
    }
}