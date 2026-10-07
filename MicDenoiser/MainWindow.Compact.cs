using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MicDenoiser;

public partial class MainWindow
{
    private string _level = "balanced", _usagePurpose = "calls";
    private bool _compareHeld, _comparePreviousBypass;
    private GlobalHotkeys? _hotkeys;
    private bool? _lastCompactMode;
    private double _studioWidth = 1100, _studioHeight = 760;

    private void InitializeCompact()
    {
        Compact.Power += () => ToggleButton_Click(this, new RoutedEventArgs());
        Compact.Mute += ToggleMute;
        Compact.Studio += () => OpenStudio(0);
        Compact.Settings += () => OpenStudio(4);
        Compact.Diagnosis += () => { OpenStudio(2); Diagnose_Click(this, new RoutedEventArgs()); };
        Compact.Setup += ShowSetup;
        Compact.Compare += HoldOriginal;
        Compact.Level += async level => await SetIsolationLevel(level);
        Compact.Devices += async (input, output) =>
        {
            if (_starting) return;
            bool restart = _suppressor != null;
            if (restart) StopProcessing();
            InputCombo.SelectedItem = InputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Id == input);
            OutputCombo.SelectedItem = OutputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Id == output);
            SaveConfig(); RefreshCompact();
            if (restart) await StartProcessing();
        };
        Deactivated += (_, _) => HoldOriginal(false);
        SourceInitialized += (_, _) =>
        {
            try
            {
                _hotkeys = new GlobalHotkeys(new WindowInteropHelper(this).Handle, ToggleMute,
                    () => ToggleButton_Click(this, new RoutedEventArgs()));
                HotkeyStatus.Text = T(_hotkeys.MuteRegistered && _hotkeys.PowerRegistered ? "HotkeysReady" : "HotkeysConflict");
            }
            catch (Exception ex) { HotkeyStatus.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
        };
    }
    private void OpenStudio(int page)
    {
        _simpleMode = false; InterfaceModeCombo.SelectedIndex = 1;
        ApplyInterfaceMode(); MainTabs.SelectedIndex = page; SaveConfig(); RestoreFromTray();
    }
    private void StudioPage_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs)) return;
        bool sound = MainTabs.SelectedIndex == 0;
        StudioMetersCard.Visibility = HealthBanner.Visibility = sound ? Visibility.Visible : Visibility.Collapsed;
        StudioQuickMeters.Visibility = sound ? Visibility.Collapsed : Visibility.Visible;
    }
    private void UsagePurpose_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready || _loading) return;
        _usagePurpose = UsagePurposeCombo.SelectedIndex == 1 ? "studio" : "calls";
        ApplyInterfaceMode(); SaveConfig();
    }
    private void Simple_Click(object sender, RoutedEventArgs e)
    { _simpleMode = true; InterfaceModeCombo.SelectedIndex = 0; ApplyInterfaceMode(); SaveConfig(); }
    private void ToggleMute()
    {
        if (_starting || _closed || _comparison != null || _abBusy) return;
        HoldOriginal(false); _settings.Muted = !_settings.Muted;
        _suppressor?.UpdateSettings(_settings); RefreshCompact(); SaveConfig();
        _tray?.Refresh(_suppressor != null, _starting, _settings.Muted, EffectiveLevel);
    }
    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();
    private void HoldOriginal(bool pressed)
    {
        if (pressed)
        {
            if (_compareHeld || _suppressor == null || _starting || _comparison != null || _abBusy || _settings.Muted) return;
            _comparePreviousBypass = _settings.Bypass; _compareHeld = true; _settings.Bypass = true;
        }
        else
        {
            if (!_compareHeld) return;
            _compareHeld = false; _settings.Bypass = _comparePreviousBypass;
        }
        BypassCheck.IsChecked = _settings.Bypass;
        _suppressor?.UpdateSettings(_settings); RefreshCompact();
    }
    private ProcessingSettings PersistentSettings()
    {
        var result = _settings.SanitizedClone();
        if (_compareHeld) result.Bypass = _comparePreviousBypass;
        return result;
    }
    private async Task SetIsolationLevel(string level)
    {
        if (_starting || _abBusy || _comparison != null || _preview != null) return;
        HoldOriginal(false);
        var settings = SuppressionLevel.Create(level, _settings);
        bool restart = _suppressor != null && (settings.Engine != _settings.Engine || _settings.FastSinging);
        if (restart) StopProcessing();
        _level = level; _settings.CopyFrom(settings); _preset = "custom"; _abResult = null;
        ApplySettingsToUi(); _suppressor?.UpdateSettings(_settings); SaveConfig(); RefreshCompact();
        if (restart) await StartProcessing();
    }
    private string EffectiveLevel => SuppressionLevel.Matches(_level, _settings) ? _level : "custom";
    private void RefreshCompact()
    {
        if (!_ready) return;
        Compact.Refresh(_suppressor != null, _starting || _abBusy || _comparison != null || _preview != null, _settings, EffectiveLevel, StatusText.Text);
        Compact.SetDevices(InputCombo.Items.Cast<DeviceItem>().Select(d => new SimpleView.Choice(d.Id, d.Name)).ToArray(),
            OutputCombo.Items.Cast<DeviceItem>().Select(d => new SimpleView.Choice(d.Id, d.Name)).ToArray(),
            (InputCombo.SelectedItem as DeviceItem)?.Id, (OutputCombo.SelectedItem as DeviceItem)?.Id, !_starting && !_abBusy && _preview == null);
        StudioPowerButton.Content = T(_starting ? "Starting" : _suppressor == null ? "Ui019" : "Stop");
        StudioMuteButton.Content = T(_settings.Muted ? "Unmute" : "Mute");
        _tray?.Refresh(_suppressor != null, _starting || _abBusy || _comparison != null || _preview != null, _settings.Muted, EffectiveLevel);
    }
    private void ApplyCompactLayout()
    {
        if (_lastCompactMode == _simpleMode) { RefreshCompact(); return; }
        bool wasStudio = _lastCompactMode == false;
        _lastCompactMode = _simpleMode;
        if (_simpleMode && wasStudio)
        { _studioWidth = Math.Max(900, ActualWidth); _studioHeight = Math.Max(680, ActualHeight); }
        Compact.Visibility = _simpleMode ? Visibility.Visible : Visibility.Collapsed;
        StudioHeader.Visibility = StudioBody.Visibility = StudioFooter.Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        MinWidth = _simpleMode ? 400 : 900; MinHeight = _simpleMode ? 560 : 680;
        Width = _simpleMode ? 440 : Math.Min(_studioWidth, SystemParameters.WorkArea.Width - 24);
        Height = _simpleMode ? Math.Min(680, SystemParameters.WorkArea.Height - 24) : Math.Min(_studioHeight, SystemParameters.WorkArea.Height - 24);
        RefreshCompact();
    }
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(_language) { Owner = this }.ShowDialog();
    private void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        RenderDiagnostic(_lastDiagnostic);
        DiagnoseResult.Text = T("Diagnostic" + _lastDiagnostic.Concern);
        DiagnoseFixButton.Visibility = _lastDiagnostic.Concern is AudioConcern.InputGain or AudioConcern.ProcessingLoad or AudioConcern.OutputGaps or AudioConcern.OutputPressure
            ? Visibility.Visible : Visibility.Collapsed;
        DiagnoseFixButton.Content = T(_lastDiagnostic.Concern == AudioConcern.InputGain ? "ReduceGain" : _lastDiagnostic.Concern == AudioConcern.OutputPressure ? "ReduceOutputGain" : "ApplyStability");
    }
    private async void DiagnoseFix_Click(object sender, RoutedEventArgs e)
    {
        if (_starting || _comparison != null || _abBusy) return;
        if (_lastDiagnostic.Concern == AudioConcern.InputGain) ReduceGain_Click(sender, e);
        else if (_lastDiagnostic.Concern == AudioConcern.OutputPressure) OutputGainSlider.Value = Math.Max(-12, OutputGainSlider.Value - 3);
        else if (_lastDiagnostic.Concern is AudioConcern.ProcessingLoad or AudioConcern.OutputGaps)
        {
            var concern = _lastDiagnostic.Concern;
            bool restart = _suppressor != null; StopProcessing();
            if (concern == AudioConcern.ProcessingLoad) _settings.Engine = DenoiserKind.RNNoise;
            _settings.BufferMode = AudioBufferMode.Stable; ApplySettingsToUi(); SaveConfig();
            if (restart) await StartProcessing();
        }
        DiagnoseResult.Text = T("FixApplied"); SaveConfig();
    }
}
