using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TrayClockApp.Core;
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
    private readonly List<IMenuItem> _menuItems;

    public MainWindow(IEnumerable<ILabel> labels, List<IMenuItem> menuItems)
    {
        InitializeComponent();

        _menuItems = menuItems;

        foreach (var label in labels) LabelHost.Children.Add(label.GetTextBlock());

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
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

    public void SetPositionFromConfig(double x, double y)
    {
        _configX = x;
        _configY = y;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        if (_configX is not null && _configY is not null &&
            _configX >= 0 && _configX <= Math.Max(0, workArea.Width - ActualWidth) &&
            _configY >= 0 && _configY <= Math.Max(0, workArea.Height - ActualHeight))
        {
            Left = _configX.Value;
            Top = _configY.Value;
        }
        else
        {
            Left = (workArea.Width - ActualWidth) / 3;
            Top = 0;
        }
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
        var workArea = SystemParameters.WorkArea;
        var maxX = Math.Max(0, workArea.Width - ActualWidth);
        var maxY = Math.Max(0, workArea.Height - ActualHeight);
        Left = Math.Clamp(Left + delta.X, 0, maxX);
        Top = Math.Clamp(Top + delta.Y, 0, maxY);
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