using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MicDenoiser;

public partial class MainWindow
{
    private ComparisonCapture? _comparison;
    private ComparisonResult? _comparisonResult;
    private WasapiOut? _preview;
    private MMDevice? _previewDevice;
    private DispatcherTimer? _comparisonTimer;
    private AudioDeviceWatcher? _deviceWatcher;
    private StabilityAdvice _advice = StabilityAdvice.Observing;
    private bool _previewOriginal;
    private bool _darkTheme, _startWithWindows, _savedDevicesAvailable, _gatePreserved;
    private int _deviceGeneration;

    private void InitializeFeatures()
    {
        try { _startWithWindows = StartupRegistration.IsEnabled(); } catch { _startWithWindows = false; }
        ApplySettingsToUi();
        AppTheme.Apply(this, _darkTheme); _tray?.SetDarkTheme(_darkTheme);
        SourceInitialized += (_, _) => AppTheme.ApplyTitleBar(this, _darkTheme);
        _comparisonTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _comparisonTimer.Tick += (_, _) => RefreshComparison();
        try
        {
            _deviceWatcher = new AudioDeviceWatcher((id, lost) =>
            {
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { if (!_closed) OnDeviceChanged(id, lost); });
            });
        }
        catch { StatusText.Text = T("DeviceWatchUnavailable"); }
        Loaded += async (_, _) =>
        {
            bool startup = Environment.GetCommandLineArgs().Contains("--startup");
            if (!_setupCompleted && !startup) ShowSetup();
            if (!_startWithWindows || !startup) return;
            TrayMinimize_Click(this, new RoutedEventArgs());
            if (_savedDevicesAvailable) await StartProcessing();
            else ShowDeviceRecovery("StartupMissingDevices");
        };
        ResetStudioMeters(); RefreshAdvice(); RefreshComparison();
        RenderDiagnostic(default); SetDeviceControls();
    }

    private void ShowDeviceRecovery(string key)
    {
        DeviceRecoveryPanel.Visibility = Visibility.Visible;
        DeviceRecoveryText.SetResourceReference(TextBlock.TextProperty, key);
        if (!IsVisible) _tray?.ShowNotice(T("DeviceRecoveryTitle"), T(key), error: true);
    }
    private void OnDeviceChanged(string id, bool lost)
    {
        bool selected = (InputCombo.SelectedItem as DeviceItem)?.Id == id || (OutputCombo.SelectedItem as DeviceItem)?.Id == id;
        if (lost && _previewDevice?.ID == id) StopPreview();
        if (lost && selected)
        {
            _deviceGeneration++;
            StopProcessing(); ShowDeviceRecovery("DeviceDisconnected");
        }
        if (_suppressor == null && !_starting)
        {
            try { LoadDevices(); } catch (Exception ex) { StatusText.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
        }
    }
    private void RecoverDevices_Click(object sender, RoutedEventArgs e)
    {
        if (_starting) return;
        StopPreview(); StopProcessing(); RefreshDevices_Click(sender, e);
        InputCombo.Focus(); StatusText.Text = T("DeviceChooseAgain");
    }
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _darkTheme = DarkThemeCheck.IsChecked == true;
        AppTheme.Apply(this, _darkTheme); EqPlot.InvalidateVisual();
        _tray?.SetDarkTheme(_darkTheme);
        ToggleButton.Background = (System.Windows.Media.Brush)FindResource(_suppressor == null ? "PrimaryAccent" : "Danger");
        if (_suppressor == null) ResetMeters(); else { lock (_meterLock) _hasMeters = true; }
        SaveConfig();
    }
    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = StartupCheck.IsChecked == true;
        try { StartupRegistration.SetEnabled(enabled); _startWithWindows = enabled; SaveConfig(); }
        catch (Exception ex) { StartupCheck.IsChecked = _startWithWindows; StatusText.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
    private void RefreshAdvice()
    {
        AdviceText.Text = T(_settings.BufferMode == AudioBufferMode.Automatic ? "Advice" + _advice : "AdviceEnableAuto");
        ApplyAdviceButton.Visibility = _settings.BufferMode == AudioBufferMode.Automatic
            && _advice is StabilityAdvice.MoreBuffer or StabilityAdvice.Lightweight ? Visibility.Visible : Visibility.Collapsed;
        ApplyAdviceButton.IsEnabled = !_starting && _comparison == null && _preview == null && !_abBusy;
    }
    private async void ApplyAdvice_Click(object sender, RoutedEventArgs e)
    {
        if (_starting || _comparison != null || _preview != null || _abBusy) return;
        var advice = _advice;
        StopProcessing();
        if (advice == StabilityAdvice.MoreBuffer) _settings.BufferMode = AudioBufferMode.Stable;
        else if (advice == StabilityAdvice.Lightweight) { _settings.Engine = DenoiserKind.RNNoise; _settings.GateEnabled = false; }
        else return;
        _advice = StabilityAdvice.Observing; ApplySettingsToUi(); RefreshAdvice(); SaveConfig();
        await StartProcessing();
    }

    private async void RecordComparison_Click(object sender, RoutedEventArgs e)
    {
        if (_starting || _comparison != null || _preview != null) return;
        if (_settings.Bypass) { ComparisonStatus.Text = T("RecordDisableBypass"); return; }
        if (_suppressor == null) await StartProcessing();
        if (_suppressor == null || _closed) return;
        try
        {
            _comparisonResult = null;
            _abResult = null; _abError = null;
            _comparison = _suppressor.BeginComparison();
            _comparisonTimer!.Start(); RefreshComparison(); SetDeviceControls();
        }
        catch (Exception ex) { ComparisonStatus.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
    private void CancelComparison_Click(object sender, RoutedEventArgs e) { CancelComparison(); SetDeviceControls(); }
    private void CancelComparison()
    {
        var capture = _comparison; _comparison = null;
        _suppressor?.CancelComparison(); capture?.Cancel(); _comparisonTimer?.Stop();
        RefreshComparison();
        if (capture != null) ComparisonStatus.Text = T("RecordCanceled");
    }
    private async void CompleteComparison(NoiseSuppressor source, ComparisonCapture capture)
    {
        if (_closed || _suppressor != source || _comparison != capture) return;
        _comparisonTimer?.Stop(); ComparisonStatus.Text = T("RecordMatching");
        try
        {
            var result = await Task.Run(() => ComparisonResult.Create(capture));
            if (_closed || _comparison != capture) return;
            _comparison = null; _comparisonResult = result;
            RefreshComparison(); SetDeviceControls();
            if (!IsVisible) _tray?.ShowNotice(T("RecordTitle"), T("RecordReady"));
        }
        catch (Exception ex)
        {
            CancelComparison(); SetDeviceControls(); ComparisonStatus.Text = T("Error", UiStrings.ErrorDetail(ex.Message));
        }
    }
    private void RefreshComparison()
    {
        ComparisonProgress.Value = _comparison?.SampleCount / 48000.0 ?? (_comparisonResult != null ? 10 : 0);
        if (_comparison != null) ComparisonStatus.Text = T(_comparison.IsComplete ? "RecordMatching" : "RecordProgress", _comparison.SampleCount / 48000.0);
        else ComparisonStatus.Text = T(_preview != null ? _previewTextKey : _comparisonResult == null ? "RecordIdle" : "RecordReady");
        bool busy = _comparison != null || _starting || _preview != null || _abBusy;
        RecordButton.IsEnabled = !busy;
        CancelRecordButton.IsEnabled = _comparison != null;
        PlayOriginalButton.IsEnabled = PlayProcessedButton.IsEnabled = _comparisonResult != null && !busy;
        StopPreviewButton.IsEnabled = _preview != null;
        ExportOriginalButton.IsEnabled=ExportProcessedButton.IsEnabled=_comparisonResult!=null && !busy && !_exporting;
        PreviewOutputCombo.IsEnabled = !busy;
        SoundOptions.IsEnabled = CustomOptions.IsEnabled = _comparison == null && !_abBusy;
        ReduceGainButton.IsEnabled = _comparison == null && !_abBusy;
        RefreshAb();
        RefreshAdvice();
    }
    private void PlayOriginal_Click(object sender, RoutedEventArgs e) => PlayComparison(true);
    private void PlayProcessed_Click(object sender, RoutedEventArgs e) => PlayComparison(false);
    private void PlayComparison(bool original)
    {
        if (_comparisonResult != null) PlayResult(_comparisonResult, original, original ? "PlayingOriginal" : "PlayingProcessed");
    }
    private void PlayResult(ComparisonResult result, bool original, string label)
    {
        if (_comparison != null || _starting || _abBusy || PreviewOutputCombo.SelectedItem is not DeviceItem device) return;
        // The visible test instructions explain that listening pauses live denoising.
        StopProcessing(); StopPreview();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _previewDevice = enumerator.GetDevice(device.Id);
            var playback = new WasapiOut(_previewDevice, AudioClientShareMode.Shared, true, 60);
            _preview = playback; _previewOriginal = original; _previewTextKey = label;
            playback.Init(result.CreatePlayback(original).ToWaveProvider());
            playback.PlaybackStopped += (_, args) => Dispatcher.BeginInvoke(() =>
            {
                if (_preview != playback || _closed) return;
                StopPreview();
                if (args.Exception != null) ComparisonStatus.Text = T("Error", UiStrings.ErrorDetail(args.Exception.Message));
            });
            playback.Play(); RefreshComparison(); SetDeviceControls();
            ComparisonStatus.Text = T(label);
        }
        catch (Exception ex) { StopPreview(); ComparisonStatus.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
    private void StopPreview_Click(object sender, RoutedEventArgs e) => StopPreview();
    private void StopPreview()
    {
        var output = _preview; _preview = null;
        try { output?.Stop(); } catch { }
        try { output?.Dispose(); } catch { }
        try { _previewDevice?.Dispose(); } catch { }
        _previewDevice = null;
        RefreshComparison(); SetDeviceControls();
    }
    private void DisposeFeatures()
    {
        _comparisonTimer?.Stop(); CancelComparison(); StopPreview();
        _abCancellation?.Cancel();
        try { _deviceWatcher?.Dispose(); } catch { }
        _deviceWatcher = null; _comparisonResult = null;
    }
}
