using System.Diagnostics;
using System.Windows;
using NAudio.CoreAudioApi;

namespace MicDenoiser;

public partial class SetupWindow : Window
{
    public SetupWindow(string language)
    {
        InitializeComponent(); UiStrings.Apply(this, language); Detect();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
    }
    private void Detect()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            var endpoints = devices.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
            bool input = false, output = false;
            foreach (var endpoint in endpoints)
                using (endpoint)
                {
                    input |= endpoint.DataFlow == DataFlow.Render && endpoint.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase);
                    output |= endpoint.DataFlow == DataFlow.Capture && endpoint.FriendlyName.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase);
                }
            CableStatus.Text = UiStrings.Get(input && output ? "SetupFound" : "SetupMissing");
        }
        catch (Exception ex) { CableStatus.Text = UiStrings.Get("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
    private void Vendor_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true }); }
        catch (Exception ex) { CableStatus.Text = UiStrings.Get("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
    private void Detect_Click(object sender, RoutedEventArgs e) => Detect();
    private void Done_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
