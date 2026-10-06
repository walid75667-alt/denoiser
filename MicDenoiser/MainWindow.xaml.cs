using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace MicDenoiser;

public partial class MainWindow : Window
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicDenoiser", "settings.json");
    private readonly ProcessingSettings _settings = new();
    private readonly object _meterLock = new();
    private readonly DispatcherTimer _meterTimer;
    private NoiseSuppressor? _suppressor, _meterSource;
    private NotificationTray? _tray;
    private bool _minimizeToTray = true, _trayNoticeShown;
    private WindowState _restoreWindowState = WindowState.Normal;
    private MeterData _latestMeters;
    private bool _hasMeters, _ready, _loading, _starting, _closed;
    private string _preset = "studio", _language = "ar";
    private int _lastUnderruns;
    private DateTime _gapUntil, _clippingUntil, _lastMeterAt;
    private double _inDisp, _outDisp;
    private static string T(string key, params object[] args) => UiStrings.Get(key, args);

    public MainWindow()
    {
        InitializeComponent();
        UiStrings.Apply(this, _language);
        try { LoadDevices(); } catch (Exception ex) { StatusText.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
        LoadConfig();
        UiStrings.Apply(this, _language);
        _ready = true;
        ApplySettingsToUi();
        if (!OutputCombo.Items.Cast<DeviceItem>().Any(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)))
            StatusText.Text = T("NoCable");
        _meterTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _meterTimer.Tick += (_, _) =>
        {
            MeterData m; NoiseSuppressor? source;
            lock (_meterLock)
            {
                if (!_hasMeters)
                {
                    if (_suppressor != null && DateTime.UtcNow - _lastMeterAt > TimeSpan.FromMilliseconds(750))
                    {
                        HealthBanner.Background = (Brush)FindResource("WarningSoft");
                        HealthText.Text = T("WaitingData"); HealthDetail.Text = T("WaitingDataDetail");
                    }
                    return;
                }
                source = _meterSource; m = _latestMeters; _hasMeters = false;
            }
            if (_suppressor == source && source != null) RenderMeters(m);
        };
        _meterTimer.Start();
        InitializeTray();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized && IsVisible) _restoreWindowState = WindowState;
            else if (_minimizeToTray) HideToTray();
        };
        SizeChanged += (_, _) => { SetMask(InMask, InTrack, _inDisp); SetMask(OutMask, OutTrack, _outDisp); };
        Closing += (_, _) =>
        {
            _closed = true; _meterTimer.Stop();
            try { StopProcessing(); }
            finally { _tray?.Dispose(); _tray = null; SaveConfig(); }
        };
    }

    private void InitializeTray()
    {
        void Post(Action action)
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(() => { if (!_closed) action(); });
        }
        try
        {
            _tray = new NotificationTray(() => Post(RestoreFromTray),
                () => Post(() => ToggleButton_Click(this, new RoutedEventArgs())), () => Post(Close));
        }
        catch (Exception) { StatusText.Text = T("TrayUnavailable"); }
        TrayMinimizeButton.IsEnabled = MinimizeToTrayCheck.IsEnabled = _tray != null;
        MinimizeToTrayCheck.IsChecked = _tray != null && _minimizeToTray;
    }

    private void TrayMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        HideToTray(); // The explicit command works regardless of the title-bar minimize preference.
    }
    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        _minimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        SaveConfig();
    }
    private void HideToTray()
    {
        if (_tray == null || _closed || !IsVisible) return;
        _tray.Refresh(_suppressor != null, _starting);
        ShowInTaskbar = false;
        Hide();
        _meterTimer.Stop(); // Meter events coalesce; the dedicated audio worker keeps running.
        SaveConfig();
        if (!_trayNoticeShown)
        {
            _trayNoticeShown = true;
            _tray.ShowNotice(T("TrayHiddenTitle"), T("TrayHiddenInfo"));
        }
    }
    private void RestoreFromTray()
    {
        if (_closed) return;
        ShowInTaskbar = true;
        WindowState = _restoreWindowState;
        Show();
        _meterTimer.Start();
        Activate();
    }

    private void LoadDevices()
    {
        string? inputId = (InputCombo.SelectedItem as DeviceItem)?.Id;
        string? outputId = (OutputCombo.SelectedItem as DeviceItem)?.Id;
        using var devices = new MMDeviceEnumerator();
        var inputs = new List<DeviceItem>(); var outputs = new List<DeviceItem>();
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        { using (device) inputs.Add(new DeviceItem(device.ID, device.FriendlyName)); }
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        { using (device) outputs.Add(new DeviceItem(device.ID, device.FriendlyName)); }
        InputCombo.ItemsSource = inputs; OutputCombo.ItemsSource = outputs;
        InputCombo.SelectedItem = inputs.FirstOrDefault(d => d.Id == inputId) ?? inputs.FirstOrDefault();
        OutputCombo.SelectedItem = outputs.FirstOrDefault(d => d.Id == outputId)
            ?? outputs.FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)) ?? outputs.FirstOrDefault();
        if (!outputs.Any(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))) StatusText.Text = T("NoCable");
    }
    private void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressor != null || _starting) return;
        try { LoadDevices(); SaveConfig(); } catch (Exception ex) { StatusText.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loading || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string language }) return;
        _language = language; UiStrings.Apply(this, language); ApplySettingsToUi();
        SetStatus(_suppressor != null);
        StatusText.Text = _starting ? T("Starting") : _suppressor != null ? T("ActiveStatus", _suppressor.EngineName) : T("Ui062");
        RefreshFormat();
        if (_suppressor == null) ResetMeters();
        else { lock (_meterLock) _hasMeters = true; }
        SaveConfig();
    }
    private void SetPreset(string key)
    {
        _preset = key;
        bool bypass = _settings.Bypass, gate = _settings.GateEnabled;
        var mode = _settings.BufferMode;
        _settings.CopyFrom(ProcessingSettings.FromPreset(key, _settings.Engine));
        _settings.Bypass = bypass; _settings.BufferMode = mode;
        if (_suppressor != null || _starting) _settings.GateEnabled = gate; // Never allocate a new VAD model on the live worker.
        ApplySettingsToUi(); _suppressor?.UpdateSettings(_settings);
    }
    private void Preset_Checked(object sender, RoutedEventArgs e)
    {
        if (_ready && !_loading && sender is RadioButton { Tag: string key }) SetPreset(key);
    }
    private void SofterSound_Click(object sender, RoutedEventArgs e) => SetPreset("natural");
    private void ResetAudio_Click(object sender, RoutedEventArgs e) => SetPreset("studio");
    private void Engine_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loading || _suppressor != null || _starting) return;
        if (EngineCombo.SelectedItem is not ComboBoxItem { Tag: string kind } || !Enum.TryParse<DenoiserKind>(kind, out var engine)) return;
        _settings.Engine = engine;
        if (_preset != "custom") SetPreset(_preset);
        else { _settings.GateEnabled = engine == DenoiserKind.RNNoise; ApplySettingsToUi(); }
    }
    private void BufferMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loading || _suppressor != null || _starting) return;
        if (BufferModeCombo.SelectedIndex is >= 0 and <= 2) _settings.BufferMode = (AudioBufferMode)BufferModeCombo.SelectedIndex;
        RefreshBufferHint();
    }
    private void RefreshBufferHint() => BufferHint.Text = T(_settings.BufferMode switch
        { AudioBufferMode.Stable => "StableHint", AudioBufferMode.LowLatency => "FastHint", _ => "BalancedHint" });
    private void Param_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ReadUiToSettings();
    private void Param_Clicked(object sender, RoutedEventArgs e) => ReadUiToSettings();
    private void ApplySettingsToUi()
    {
        _loading = true;
        try
        {
            var s = _settings;
            BrandText.HorizontalAlignment = _language == "ar" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            BrandPanel.HorizontalAlignment = BrandText.HorizontalAlignment;
            MinimizeToTrayCheck.IsChecked = _tray != null && _minimizeToTray;
            LanguageCombo.SelectedIndex = _language == "en" ? 1 : 0;
            BufferModeCombo.SelectedIndex = (int)s.BufferMode;
            StrengthSlider.Value = s.Strength * 100; NoiseLimitSlider.Value = s.NoiseReductionDb;
            NoiseLimitSlider.IsEnabled = s.Engine == DenoiserKind.DeepFilterNet3;
            GateSlider.Value = (s.GateThreshold - 0.15) / 0.70 * 100; GateDepthSlider.Value = s.GateDepthDb;
            EngineCombo.SelectedIndex = s.Engine == DenoiserKind.DeepFilterNet3 ? 0 : 1;
            GateEnabledCheck.IsChecked = s.GateEnabled; HighPassCheck.IsChecked = s.HighPassEnabled;
            GateSlider.IsEnabled = GateDepthSlider.IsEnabled = s.GateEnabled;
            InputGainSlider.Value = s.InputGainDb; PresenceSlider.Value = s.PresenceDb; MudSlider.Value = s.MudCutDb;
            CompCheck.IsChecked = s.CompressorOn; CompThresholdSlider.Value = s.CompThresholdDb;
            CompRatioSlider.Value = s.CompRatio; OutputGainSlider.Value = s.OutputGainDb; BypassCheck.IsChecked = s.Bypass;
            foreach (var rb in PresetRadios()) rb.IsChecked = (string?)rb.Tag == _preset;
            RefreshPresetHint(); RefreshBufferHint(); RefreshRouting();
        }
        finally { _loading = false; }
    }
    private void RefreshPresetHint() => PresetHint.Text = T("Preset" + _preset)
        + ((_suppressor != null || _starting) && _preset != "custom" ? T("PreservedGate") : "");
    private void OutputDevice_Changed(object sender, SelectionChangedEventArgs e) => RefreshRouting();
    private void RefreshRouting() => RoutingText.Text = T(OutputCombo.SelectedItem is DeviceItem item
        && item.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ? "Ui009" : "DirectRouting");
    private void ReadUiToSettings()
    {
        if (!_ready || _loading) return;
        var s = _settings;
        s.Strength = (float)(StrengthSlider.Value / 100); s.NoiseReductionDb = (float)NoiseLimitSlider.Value;
        s.GateThreshold = (float)(.15 + GateSlider.Value / 100 * .70); s.GateDepthDb = (float)GateDepthSlider.Value;
        s.GateEnabled = GateEnabledCheck.IsChecked == true; s.HighPassEnabled = HighPassCheck.IsChecked == true;
        GateSlider.IsEnabled = GateDepthSlider.IsEnabled = s.GateEnabled;
        s.InputGainDb = (float)InputGainSlider.Value; s.PresenceDb = (float)PresenceSlider.Value;
        s.MudCutDb = (float)MudSlider.Value; s.CompressorOn = CompCheck.IsChecked == true;
        s.CompThresholdDb = (float)CompThresholdSlider.Value; s.CompRatio = (float)CompRatioSlider.Value;
        s.OutputGainDb = (float)OutputGainSlider.Value; s.Bypass = BypassCheck.IsChecked == true;
        if (IsCustomSound())
        {
            _preset = "custom"; _loading = true;
            foreach (var rb in PresetRadios()) rb.IsChecked = false;
            _loading = false; RefreshPresetHint();
        }
        _suppressor?.UpdateSettings(s);
    }
    // Comparing against a preset keeps a bypass-only comparison from relabeling the sound profile.
    private bool IsCustomSound()
    {
        if (_preset == "custom") return false;
        var p = ProcessingSettings.FromPreset(_preset, _settings.Engine);
        p.BufferMode = _settings.BufferMode; p.Bypass = _settings.Bypass;
        if (_suppressor != null || _starting) p.GateEnabled = _settings.GateEnabled;
        return JsonSerializer.Serialize(p) != JsonSerializer.Serialize(_settings);
    }
    private IEnumerable<RadioButton> PresetRadios() => PresetPanel.Children.OfType<RadioButton>();
    private void SetDeviceControls()
    {
        bool idle = _suppressor == null && !_starting;
        InputCombo.IsEnabled = OutputCombo.IsEnabled = EngineCombo.IsEnabled = BufferModeCombo.IsEnabled = RefreshDevicesButton.IsEnabled = GateEnabledCheck.IsEnabled = idle;
        ToggleButton.IsEnabled = !_starting;
        LoadingProgress.Visibility = _starting ? Visibility.Visible : Visibility.Collapsed;
        _tray?.Refresh(_suppressor != null, _starting);
    }
    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_starting) return;
        if (_suppressor == null) await StartProcessing(); else StopProcessing();
    }
    private async Task StartProcessing()
    {
        if (InputCombo.SelectedItem is not DeviceItem input || OutputCombo.SelectedItem is not DeviceItem output)
        { StatusText.Text = T("ChooseDevices"); return; }
        ReadUiToSettings(); _starting = true; SetDeviceControls();
        StatusText.Text = T("Starting");
        try
        {
            var settings = _settings.SanitizedClone();
            var suppressor = await Task.Run(() => new NoiseSuppressor(input.Id, output.Id, settings));
            if (_closed) { suppressor.Dispose(); return; }
            _suppressor = suppressor; suppressor.UpdateSettings(_settings);
            lock (_meterLock) _lastMeterAt = DateTime.UtcNow;
            suppressor.Meters += m => { lock (_meterLock) { _meterSource = suppressor; _latestMeters = m; _hasMeters = true; _lastMeterAt = DateTime.UtcNow; } };
            suppressor.Failed += error => Dispatcher.BeginInvoke(() =>
            {
                if (_suppressor != suppressor) return;
                StopProcessing(); StatusText.Text = T("Failed", UiStrings.ErrorDetail(error.Message));
                if (!IsVisible) _tray?.ShowNotice(T("TrayAudioStopped"), UiStrings.ErrorDetail(error.Message), error: true);
            });
            _lastUnderruns = 0; _gapUntil = _clippingUntil = DateTime.MinValue;
            suppressor.Start(); ToggleButton.Content = T("Stop"); ToggleButton.Background = (Brush)FindResource("Danger");
            SetStatus(true); StatusText.Text = T("ActiveStatus", suppressor.EngineName);
            RefreshFormat(); RefreshPresetHint(); SaveConfig();
        }
        catch (Exception ex)
        {
            _suppressor?.Dispose(); _suppressor = null; ResetMeters(); SetStatus(false);
            StatusText.Text = T("Error", UiStrings.ErrorDetail(ex.Message));
            if (!IsVisible) _tray?.ShowNotice(T("TrayAudioStopped"), UiStrings.ErrorDetail(ex.Message), error: true);
        }
        finally { _starting = false; SetDeviceControls(); }
    }
    private void StopProcessing()
    {
        var suppressor = _suppressor; _suppressor = null;
        suppressor?.Dispose(); SetDeviceControls(); ResetMeters(); SetStatus(false);
        RefreshPresetHint(); StatusText.Text = T("Stopped");
    }
    private void RefreshFormat() => FormatText.Text = _suppressor == null ? "" : T("Format", _suppressor.InputFormatDescription,
        _suppressor.AlgorithmicDelayMs, _suppressor.PlaybackBufferTargetMs);
    private void SetStatus(bool running)
    {
        StatusPill.Text = T(running ? "Running" : "Ui001");
        StatusDot.Fill = (Brush)FindResource(running ? "Accent" : "Muted");
        ToggleButton.Content = T(running ? "Stop" : "Ui019");
        _tray?.Refresh(running, _starting);
    }
    private void ResetMeters()
    {
        _inDisp = _outDisp = 0; SetMask(InMask, InTrack, 0); SetMask(OutMask, OutTrack, 0);
        InDbText.Text = OutDbText.Text = "— dBFS";
        VoiceDot.Fill = (Brush)FindResource("Muted"); VoiceText.Text = T("Ui025"); GateText.Text = "";
        PerformanceText.Text = T("Ui057"); FormatText.Text = "";
        HealthBanner.Background = (Brush)FindResource("AccentSoft"); HealthText.Text = T("Ui021"); HealthDetail.Text = T("Ui022");
        ToggleButton.Background = (Brush)FindResource("Accent");
    }
    private void RenderMeters(MeterData m)
    {
        _inDisp = Math.Max(ToMeter(m.InPeak), _inDisp * .88); _outDisp = Math.Max(ToMeter(m.OutPeak), _outDisp * .88);
        SetMask(InMask, InTrack, _inDisp); SetMask(OutMask, OutTrack, _outDisp);
        InDbText.Text = PeakText(m.InPeak); OutDbText.Text = PeakText(m.OutPeak);
        PerformanceText.Text = T("Metrics", m.FrameMs, m.QueuedMs, m.Underruns);
        if (m.Underruns > _lastUnderruns) _gapUntil = DateTime.UtcNow.AddSeconds(3);
        _lastUnderruns = m.Underruns;
        if (m.InPeak >= .98f) _clippingUntil = DateTime.UtcNow.AddSeconds(2);
        string health = DateTime.UtcNow < _clippingUntil ? "Clipping" : DateTime.UtcNow < _gapUntil ? "Gap" : m.FrameMs >= 8 ? "Slow" : "Healthy";
        HealthBanner.Background = (Brush)FindResource(health == "Healthy" ? "AccentSoft" : "WarningSoft");
        HealthText.Text = T(health); HealthDetail.Text = T(health + "Detail");
        bool voice = m.Vad >= _settings.GateThreshold;
        VoiceDot.Fill = (Brush)FindResource(voice ? "Accent" : "Muted");
        VoiceText.Text = T(m.Bypass ? "Bypassed" : m.Vad == null ? "GateOff" : voice ? "Voice" : "Silence");
        double gateDb = 20 * Math.Log10(Math.Max(m.GateGain, .0001f));
        GateText.Text = !_settings.GateEnabled || m.Bypass ? "" : gateDb > -1 ? T("GateOpen") : T("GateDb", gateDb);
    }
    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // No device identifiers or microphone recordings are included.
            Clipboard.SetText($"MicDenoiser 2.0\n{FormatText.Text}\n{PerformanceText.Text}\n{HealthText.Text}\nEngine: {_settings.Engine}\nBuffer: {_settings.BufferMode}\nNoise limit: {_settings.NoiseReductionDb:0} dB\nInput gain: {_settings.InputGainDb:0} dB\nGate: {_settings.GateEnabled}\nBypass: {_settings.Bypass}");
            StatusText.Text = T("Copied");
        }
        catch (Exception ex) { StatusText.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
    }
    private static string PeakText(float peak) => peak <= .001f ? "≤ -60 dBFS" : $"{20 * Math.Log10(peak):0.0} dBFS";
    private static double ToMeter(float lin) => lin <= .001f ? 0 : Math.Clamp((20 * Math.Log10(lin) + 60) / 60, 0, 1);
    private static void SetMask(Border mask, Border track, double level)
    { if (track.ActualWidth > 0) mask.Width = Math.Max(0, (1 - level) * track.ActualWidth); }
    private sealed class AppConfig
    {
        public string? InputDevice { get; set; }
        public string? OutputDevice { get; set; }
        public string? InputDeviceId { get; set; }
        public string? OutputDeviceId { get; set; }
        public string Preset { get; set; } = "studio";
        public string Language { get; set; } = "ar";
        public bool MinimizeToTray { get; set; } = true;
        public ProcessingSettings? Settings { get; set; }
    }
    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)); if (cfg == null) return;
            _preset = cfg.Preset is "natural" or "studio" or "podcast" or "max" or "custom" ? cfg.Preset : "studio";
            _language = cfg.Language == "en" ? "en" : "ar";
            _minimizeToTray = cfg.MinimizeToTray;
            if (cfg.Settings != null) _settings.CopyFrom(cfg.Settings.SanitizedClone());
            InputCombo.SelectedItem = InputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Id == cfg.InputDeviceId)
                ?? InputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Name == cfg.InputDevice) ?? InputCombo.SelectedItem;
            OutputCombo.SelectedItem = OutputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Id == cfg.OutputDeviceId)
                ?? OutputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Name == cfg.OutputDevice) ?? OutputCombo.SelectedItem;
        }
        catch { /* A corrupt configuration must not prevent startup. */ }
    }
    private void SaveConfig()
    {
        try
        {
            var cfg = new AppConfig { InputDevice = (InputCombo.SelectedItem as DeviceItem)?.Name,
                OutputDevice = (OutputCombo.SelectedItem as DeviceItem)?.Name,
                InputDeviceId = (InputCombo.SelectedItem as DeviceItem)?.Id, OutputDeviceId = (OutputCombo.SelectedItem as DeviceItem)?.Id,
                Preset = _preset, Language = _language, MinimizeToTray = _minimizeToTray, Settings = _settings.SanitizedClone() };
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Best effort. */ }
    }
    private sealed record DeviceItem(string Id, string Name) { public override string ToString() => Name; }
}
