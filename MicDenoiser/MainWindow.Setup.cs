using System.Windows;
namespace MicDenoiser;

public partial class MainWindow
{
    private bool _setupCompleted;
    private void Setup_Click(object sender, RoutedEventArgs e) => ShowSetup();
    private void ShowSetup()
    {
        if (_closed || _starting || _abBusy || _comparison != null) return;
        StopPreview(); StopProcessing();
        var wizard = new SetupWindow(_language, _settings, (InputCombo.SelectedItem as DeviceItem)?.Id, (OutputCombo.SelectedItem as DeviceItem)?.Id, _usagePurpose) { Owner = this };
        if (wizard.ShowDialog() == true)
        {
            _setupCompleted = true; _usagePurpose = wizard.Purpose;
            try { LoadDevices(); } catch { }
            InputCombo.SelectedItem = InputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Id == wizard.InputId) ?? InputCombo.SelectedItem;
            OutputCombo.SelectedItem = OutputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Id == wizard.OutputId) ?? OutputCombo.SelectedItem;
            _simpleMode = _usagePurpose == "calls";
            if (_usagePurpose == "studio") SetPreset("voiceover"); else { _settings.CopyFrom(SuppressionLevel.Create(_level, _settings)); _preset = "calls"; }
            ApplySettingsToUi(); SaveConfig();
        }
        if (!_closed) RefreshCompact();
    }
}
