using System.Runtime.InteropServices;

namespace TrayClockApp.Infra;

public static partial class Win32
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExLayered = 0x00080000;

    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;
    private const uint EsContinuous = 0x80000000;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    private const uint GwHwndPrev = 0x0003;

    private static readonly nint HwndTopmost = new(-1);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial void SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static void SetNativeClickThrough(nint hwnd, bool nonTransparent)
    {
        if (hwnd == nint.Zero) return;
        try
        {
            long style = GetWindowLongPtr(hwnd, GwlExStyle);
            if (nonTransparent)
            {
                style &= ~WsExTransparent;
            }
            else
            {
                style |= WsExLayered;
                style |= WsExTransparent;
            }

            SetWindowLongPtr(hwnd, GwlExStyle, (nint)style);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Win32.SetNativeClickThrough 调用异常: {ex}");
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint esFlags);

    public static bool SetKeepScreenOn(bool on)
    {
        var flags = on
            ? EsContinuous | EsSystemRequired | EsDisplayRequired
            : EsContinuous;
        return SetThreadExecutionState(flags) != 0;
    }

    public static bool SetKeepSystemRun(bool run)
    {
        var flags = run
            ? EsContinuous | EsSystemRequired
            : EsContinuous;
        return SetThreadExecutionState(flags) != 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    public static void PinToTop(IntPtr hwnd)
    {
        if (hwnd == nint.Zero) return;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetWindow(nint hWnd, uint uCmd);

    public static bool IsTopOfZOrder(nint hwnd)
    {
        if (hwnd == nint.Zero) return true;
        return GetWindow(hwnd, GwHwndPrev) == nint.Zero;
    }
}