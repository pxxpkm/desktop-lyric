using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DesktopLyric.Views;

/// <summary>
/// Taskbar membership without toggling WPF ShowInTaskbar at runtime.
/// Changing that DP on a layered window re-registers the HWND and leaves
/// ghost buttons; auto-hide taskbar then shows several identical icons.
/// </summary>
internal static class ShellWindow
{
    private const int GwlExStyle = -20;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    public static void NeverInTaskbar(Window w)
    {
        w.ShowInTaskbar = false;
        if (w.IsLoaded)
            Apply(w, exclude: true);
        w.SourceInitialized -= OnNever;
        w.SourceInitialized += OnNever;
    }

    private static void OnNever(object? sender, EventArgs e)
    {
        if (sender is Window w)
            Apply(w, exclude: true);
    }

    public static void Unpin(Window w) => Apply(w, exclude: true);

    public static void Pin(Window w) => Apply(w, exclude: false);

    private static void Apply(Window w, bool exclude)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).EnsureHandle();
            if (hwnd == IntPtr.Zero) return;
            var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            if (exclude)
                ex = (ex | WsExToolWindow) & ~WsExAppWindow;
            else
                ex = (ex | WsExAppWindow) & ~WsExToolWindow;
            SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)ex);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }
        catch { }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);
}
