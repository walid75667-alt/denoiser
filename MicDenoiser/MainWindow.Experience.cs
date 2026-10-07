using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MicDenoiser;

public partial class MainWindow
{
    private bool _simpleMode = true, _abBusy;
    private ProcessingSettings? _snapshotA, _snapshotB;
    private ComparisonResult? _abResult;
    private CancellationTokenSource? _abCancellation;
    private string? _abError;
    private string _previewTextKey = "PlayingOriginal";
    private DiagnosticSnapshot _lastDiagnostic;
    private ProcessingSettings? _diagnosticSettings;

    private void InterfaceMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loading) return;
        _simpleMode = InterfaceModeCombo.SelectedIndex == 0;
        ApplyInterfaceMode(); SaveConfig();
    }
    private void ApplyInterfaceMode()
    {
        MixerTab.Visibility = EngineOptionsCard.Visibility = Visibility.Visible;
        QuickLevelsCard.Visibility = Visibility.Collapsed;
        foreach (var rb in PresetRadios())
            rb.Visibility = _usagePurpose == "studio" || (string?)rb.Tag is "natural" or "calls" or "streaming" or "weakmic" or "whisper" || (string?)rb.Tag == _preset ? Visibility.Visible : Visibility.Collapsed;
        ApplyCompactLayout();
    }
    private void FastSinging_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressor != null || _starting || _preview != null || _abBusy) { ApplySettingsToUi(); return; }
        _settings.FastSinging = FastSingingCheck.IsChecked == true;
        _abResult = null; _abError = null;
        if (!_settings.FastSinging) _settings.Strength = 1;
        _settings.CopyFrom(_settings.SanitizedClone()); _preset = "custom";
        ApplySettingsToUi(); SetDeviceControls(); SaveConfig();
    }
    private void SaveA_Click(object sender, RoutedEventArgs e) => SaveSnapshot(true);
    private void SaveB_Click(object sender, RoutedEventArgs e) => SaveSnapshot(false);
    private void SaveSnapshot(bool first)
    {
        if (_abBusy || _comparison != null) return;
        ReadUiToSettings();
        if (first) _snapshotA = _settings.SanitizedClone(); else _snapshotB = _settings.SanitizedClone();
        _abResult = null; _abError = null; RefreshAb(); SaveConfig();
    }
    private void ApplyA_Click(object sender, RoutedEventArgs e) => ApplySnapshot(_snapshotA);
    private void ApplyB_Click(object sender, RoutedEventArgs e) => ApplySnapshot(_snapshotB);
    private void ApplySnapshot(ProcessingSettings? snapshot)
    {
        if (snapshot == null || _abBusy || _comparison != null) return;
        var settings = EffectsProfile.Read(EffectsProfile.Serialize(snapshot), _settings, _suppressor != null || _starting);
        _settings.CopyFrom(settings); _preset = "custom";
        ApplySettingsToUi(); _suppressor?.UpdateSettings(_settings); SetDeviceControls(); SaveConfig();
    }
    private async void RenderAb_Click(object sender, RoutedEventArgs e)
    {
        if (_abBusy || _starting || _comparison != null || _preview != null || _comparisonResult == null || _snapshotA == null || _snapshotB == null) return;
        var source = _comparisonResult.Original;
        var a = EffectsProfile.Read(EffectsProfile.Serialize(_snapshotA), _settings, false);
        var b = EffectsProfile.Read(EffectsProfile.Serialize(_snapshotB), _settings, false);
        StopProcessing(); _abBusy = true; _abError = null; _abResult = null;
        var cancellation = new CancellationTokenSource(); _abCancellation = cancellation;
        SetDeviceControls();
        try
        {
            var result = await Task.Run(() => AbComparison.Render(source, a, b, cancellation.Token));
            if (!_closed && !cancellation.IsCancellationRequested) _abResult = result;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) _abError = T("Error", UiStrings.ErrorDetail(ex.Message)); }
        finally
        {
            _abCancellation = null; cancellation.Dispose(); _abBusy = false;
            if (!_closed) SetDeviceControls();
        }
    }
    private void CancelAb_Click(object sender, RoutedEventArgs e) => _abCancellation?.Cancel();
    private void PlayA_Click(object sender, RoutedEventArgs e) { if (_abResult != null) PlayResult(_abResult, true, "PlayingA"); }
    private void PlayB_Click(object sender, RoutedEventArgs e) { if (_abResult != null) PlayResult(_abResult, false, "PlayingB"); }
    private void RefreshAb()
    {
        bool busy = _abBusy || _comparison != null || _starting || _preview != null;
        SaveAButton.IsEnabled = SaveBButton.IsEnabled = !busy;
        ApplyAButton.IsEnabled = _snapshotA != null && !busy; ApplyBButton.IsEnabled = _snapshotB != null && !busy;
        RenderAbButton.IsEnabled = _comparisonResult != null && _snapshotA != null && _snapshotB != null && !busy;
        PlayAButton.IsEnabled = PlayBButton.IsEnabled = _abResult != null && !busy;
        CancelAbButton.IsEnabled = _abBusy;
        AbStatus.Text = _abError ?? T(_abBusy ? "AbRendering" : _snapshotA == null || _snapshotB == null ? "AbSaveFirst"
            : _comparisonResult == null ? "AbRecordFirst" : _abResult == null ? "AbNeedsRender" : "AbReady");
    }
    private void RenderDiagnostic(DiagnosticSnapshot diagnostic)
    {
        _lastDiagnostic = diagnostic;
        if (diagnostic.Windows > 0) _diagnosticSettings = _settings.SanitizedClone();
        DiagnosticText.Text = T("Diagnostic" + diagnostic.Concern);
        DiagnosticCounts.Text = T("DiagnosticCounts", diagnostic.Underruns, diagnostic.SlowWindows,
            diagnostic.SourceHotWindows, diagnostic.GainHotWindows, diagnostic.OutputPressureWindows, diagnostic.WorstFrameMs);
        ReduceGainButton.Visibility = diagnostic.Concern == AudioConcern.InputGain ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ReduceGain_Click(object sender, RoutedEventArgs e)
    {
        if (_comparison != null || _abBusy) return;
        InputGainSlider.Value = Math.Max(-12, InputGainSlider.Value - 3); SaveConfig();
    }
    private void SaveDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", DefaultExt = ".json", FileName = "MicDenoiser-diagnostics.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var report = new { Version = "2.5", CreatedUtc = DateTime.UtcNow, Evidence = _lastDiagnostic, Timing = _lastTiming,
                Observation = T("Diagnostic" + _lastDiagnostic.Concern), Settings = _diagnosticSettings ?? _settings.SanitizedClone() };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = T("DiagnosticSaved");
        }
        catch (Exception ex) { StatusText.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
}
