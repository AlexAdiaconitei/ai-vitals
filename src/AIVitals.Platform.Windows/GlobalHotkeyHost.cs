using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AIVitals.Platform.Windows;

[Flags]
public enum HotkeyModifiers : uint
{
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000
}

public sealed class GlobalHotkeyHost : NativeWindow, IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const int HotkeyId = 0x4155;
    private readonly Action _action;
    private readonly SynchronizationContext _uiContext;
    private bool _registered;

    public GlobalHotkeyHost(HotkeyModifiers modifiers, Keys key, Action action)
    {
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("GlobalHotkeyHost must be created on the UI thread.");
        _action = action;
        CreateHandle(new CreateParams { Caption = "AI Vitals hotkey" });
        _registered = RegisterHotKey(Handle, HotkeyId, (uint)(modifiers | HotkeyModifiers.NoRepeat), (uint)key);
        if (!_registered)
        {
            DestroyHandle();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo registrar el atajo global.");
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage && message.WParam.ToInt32() == HotkeyId)
            _uiContext.Post(_ => _action(), null);
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
