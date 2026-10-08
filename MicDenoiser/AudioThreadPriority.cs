using System.Runtime.InteropServices;

namespace MicDenoiser;

internal sealed class AudioThreadPriority : IDisposable
{
    private readonly IntPtr _handle;
    public AudioThreadPriority()
    {
        if (OperatingSystem.IsWindows())
        {
            uint index = 0;
            _handle = AvSetMmThreadCharacteristics("Audio", ref index);
        }
    }
    public void Dispose() { if (_handle != IntPtr.Zero) AvRevertMmThreadCharacteristics(_handle); }
    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);
    [DllImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
