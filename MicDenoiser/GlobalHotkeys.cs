using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MicDenoiser;

internal sealed class GlobalHotkeys : IDisposable
{
    private const int MuteId = 0x4D01, PowerId = 0x4D02;
    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly Action _mute, _power;
    public bool MuteRegistered { get; }
    public bool PowerRegistered { get; }
    public GlobalHotkeys(IntPtr handle, Action mute, Action power)
    {
        _handle = handle; _mute = mute; _power = power;
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Window handle is not available.");
        _source.AddHook(Receive);
        MuteRegistered = RegisterHotKey(handle, MuteId, 0x4003, 0x4D); // Ctrl+Alt+M, no repeat
        PowerRegistered = RegisterHotKey(handle, PowerId, 0x4003, 0x44); // Ctrl+Alt+D, no repeat
    }
    private IntPtr Receive(IntPtr handle, int message, IntPtr id, IntPtr data, ref bool handled)
    {
        if (message == 0x0312)
        {
            if (id.ToInt32() == MuteId) { handled = true; _mute(); }
            else if (id.ToInt32() == PowerId) { handled = true; _power(); }
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (MuteRegistered) UnregisterHotKey(_handle, MuteId);
        if (PowerRegistered) UnregisterHotKey(_handle, PowerId);
        _source.RemoveHook(Receive);
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr handle, int id);
}
