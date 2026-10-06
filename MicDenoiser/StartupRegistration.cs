using Microsoft.Win32;
using System.IO;

namespace MicDenoiser;

internal static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string Command => $"\"{Environment.ProcessPath}\" --startup";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return string.Equals(key?.GetValue("MicDenoiser") as string, Command, StringComparison.OrdinalIgnoreCase);
    }
    public static void SetEnabled(bool enabled)
    {
        if (!string.Equals(Path.GetFileName(Environment.ProcessPath), "MicDenoiser.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(UiStrings.Get("StartupInstallFirst"));
        using var key = Registry.CurrentUser.CreateSubKey(Key, writable: true);
        if (enabled) key.SetValue("MicDenoiser", Command, RegistryValueKind.String);
        else key.DeleteValue("MicDenoiser", throwOnMissingValue: false);
    }
}
