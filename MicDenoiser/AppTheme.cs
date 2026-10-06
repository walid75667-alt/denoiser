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
            ["Ink"]="#E6EEF6", ["Accent"]="#50D0C3", ["AccentSoft"]="#203E43", ["WarningSoft"]="#473B25",
            ["Surface"]="#101A24", ["CardBg"]="#1B2936", ["Muted"]="#ABBDCC", ["Line"]="#354857", ["Track"]="#2B4051"
        } : new Dictionary<string, string>
        {
            ["Ink"]="#142A37", ["Accent"]="#087F8C", ["AccentSoft"]="#E6F4F3", ["WarningSoft"]="#FFF3DF",
            ["Surface"]="#F3F5F8", ["CardBg"]="#FFFFFF", ["Muted"]="#6B7280", ["Line"]="#E3E7ED", ["Track"]="#E9EDF2"
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
