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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void DestroyIcon(IntPtr hIcon);

    public static void DestroyIconHandle(nint hIcon)
    {
        if (hIcon != nint.Zero) DestroyIcon(hIcon);
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
}