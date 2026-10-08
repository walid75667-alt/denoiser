using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MicDenoiser;

internal static class AppTheme
{
    public static void Apply(Window window, bool dark)
    {
        var colors = dark ? new Dictionary<string, string>
        {
            ["Ink"]="#E8EDF2", ["Accent"]="#2BC4AB", ["AccentSoft"]="#123530", ["WarningSoft"]="#3A2D12",
            ["Surface"]="#0F1419", ["CardBg"]="#171E26", ["Muted"]="#9AA6B2", ["Line"]="#2A343F", ["Track"]="#222C36"
        } : new Dictionary<string, string>
        {
            ["Ink"]="#12181F", ["Accent"]="#0B7D6E", ["AccentSoft"]="#DCEFEB", ["WarningSoft"]="#FBEBD3",
            ["Surface"]="#EEF1F4", ["CardBg"]="#FFFFFF", ["Muted"]="#56606B", ["Line"]="#D9DFE5", ["Track"]="#E3E8ED"
        };
        foreach (var item in colors) Application.Current.Resources[item.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Value));
        ApplyTitleBar(window, dark);
    }
    public static void ApplyTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int enabled = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
