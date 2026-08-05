using System.Runtime.InteropServices;

namespace AIVitals.Platform.Windows;

public static class WindowInteraction
{
    private const int ExtendedStyleIndex = -20;
    private const long TransparentStyle = 0x00000020L;
    private const uint NoSize = 0x0001;
    private const uint NoMove = 0x0002;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private const uint FrameChanged = 0x0020;

    public static void SetClickThrough(IntPtr windowHandle, bool enabled)
    {
        if (windowHandle == IntPtr.Zero) return;
        var current = GetWindowLongPtr(windowHandle, ExtendedStyleIndex).ToInt64();
        var next = enabled ? current | TransparentStyle : current & ~TransparentStyle;
        if (next == current) return;

        SetWindowLongPtr(windowHandle, ExtendedStyleIndex, new IntPtr(next));
        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NoSize | NoMove | NoZOrder | NoActivate | FrameChanged);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
