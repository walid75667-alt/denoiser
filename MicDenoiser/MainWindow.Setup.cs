using System.Windows;
namespace MicDenoiser;

public partial class MainWindow
{
    private bool _setupCompleted;
    private void Setup_Click(object sender, RoutedEventArgs e) => ShowSetup();
    private void ShowSetup()
    {
        if (_closed || _starting) return;
        var wizard = new SetupWindow(_language) { Owner = this };
        if (wizard.ShowDialog() == true) { _setupCompleted = true; SaveConfig(); }
        if (!_closed && _suppressor == null) RefreshDevices_Click(this, new RoutedEventArgs());
    }
}
