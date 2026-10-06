using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace MicDenoiser;

public partial class MainWindow
{
    private bool _exporting;
    private void EqBandGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.UniformGrid grid)
            grid.Columns = e.NewSize.Width >= 640 ? 4 : 2;
    }
    private void MixerNumber_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        { box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); e.Handled = true; }
    }
    private void EqReset_Click(object sender, RoutedEventArgs e)
    {
        _settings.EqLowGainDb = _settings.EqBodyGainDb = _settings.EqPresenceGainDb = _settings.EqAirGainDb = 0;
        _settings.PresenceDb = _settings.MudCutDb = 0;
        _preset = "custom"; ApplySettingsToUi(); _suppressor?.UpdateSettings(_settings); SaveConfig();
    }
    private void SaveEffects_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = T("EffectsFilter"), FileName = "MicDenoiser-effects.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try { ReadUiToSettings(); File.WriteAllText(dialog.FileName, EffectsProfile.Serialize(_settings)); StatusText.Text = T("EffectsSaved"); }
        catch (Exception ex) { StatusText.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
    private void LoadEffects_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = T("EffectsFilter"), DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 65536) throw new InvalidDataException(T("EffectsInvalid"));
            var settings = EffectsProfile.Read(File.ReadAllText(dialog.FileName), _settings, _suppressor != null || _starting);
            _settings.CopyFrom(settings); _preset = "custom"; _gatePreserved = false;
            ApplySettingsToUi(); _suppressor?.UpdateSettings(_settings); SetDeviceControls(); SaveConfig(); StatusText.Text = T("EffectsLoaded");
        }
        catch (Exception ex) { StatusText.Text = T("Error", ex is ArgumentException or System.Text.Json.JsonException ? T("EffectsInvalid") : UiStrings.ErrorDetail(ex.Message)); }
    }
    private void ResetStudioMeters()
    {
        StudioInputRms.Text = T("RmsValue", "—"); StudioOutputRms.Text = T("RmsValue", "—");
        CompReductionMeter.Value = DeEsserReductionMeter.Value = LimiterReductionMeter.Value = 0;
        CompReductionText.Text = DeEsserReductionText.Text = LimiterReductionText.Text = T("ReductionValue", 0);
    }
    private void RenderStudioMeters(MeterData m)
    {
        StudioInputRms.Text = T("RmsValue", PeakText(m.InRms)); StudioOutputRms.Text = T("RmsValue", PeakText(m.OutRms));
        CompReductionMeter.Value = m.CompressorReductionDb; CompReductionText.Text = T("ReductionValue", m.CompressorReductionDb);
        DeEsserReductionMeter.Value = m.DeEsserReductionDb; DeEsserReductionText.Text = T("ReductionValue", m.DeEsserReductionDb);
        LimiterReductionMeter.Value = m.LimiterReductionDb; LimiterReductionText.Text = T("ReductionValue", m.LimiterReductionDb);
    }
    private async void ExportOriginal_Click(object sender, RoutedEventArgs e) => await ExportComparison(true);
    private async void ExportProcessed_Click(object sender, RoutedEventArgs e) => await ExportComparison(false);
    private async Task ExportComparison(bool original)
    {
        if (_comparisonResult == null || _comparison != null || _exporting) return;
        var result = _comparisonResult;
        var dialog = new SaveFileDialog { Filter = "WAV audio (*.wav)|*.wav", DefaultExt = ".wav", FileName = original ? "MicDenoiser-original.wav" : "MicDenoiser-processed.wav" };
        if (dialog.ShowDialog(this) != true) return;
        _exporting = true; RefreshComparison();
        string? status = null;
        try
        {
            await Task.Run(() => result.SaveWaveFile(dialog.FileName, original));
            status = T("WavSaved");
        }
        catch (Exception ex) { status = T("Error", UiStrings.ErrorDetail(ex.Message)); }
        finally
        {
            _exporting = false;
            if (!_closed) { RefreshComparison(); if (status != null) ComparisonStatus.Text = status; }
        }
    }
}
