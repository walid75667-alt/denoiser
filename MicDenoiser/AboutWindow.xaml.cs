using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
namespace MicDenoiser;
public partial class AboutWindow : Window
{
    public AboutWindow(string language)
    {
        InitializeComponent(); UiStrings.Apply(this, language);
        VersionText.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.6.0";
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
    }
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url }) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { ErrorText.Text = UiStrings.Get("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
}
