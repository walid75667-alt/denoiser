using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace MicDenoiser;

public partial class MainWindow : Window
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicDenoiser", "settings.json");

    private static readonly Dictionary<string, string> PresetHints = new()
    {
        ["natural"] = "عزل أخف، مع مزج صوت أصلي متوافق زمنيًا مع المعالَج.",
        ["studio"] = "عزل كامل من غير EQ أو ضغط؛ نقطة البداية للمقارنة.",
        ["podcast"] = "صوت إذاعي واضح ومضغوط قليلًا، مع وضوح أعلى للكلام.",
        ["max"] = "عزل كامل بتمريرة واحدة. بوابة الكلام اختيارية للحفاظ على الهمس."
    };

    private readonly ProcessingSettings _settings = new();
    private NoiseSuppressor? _suppressor;
    private bool _ready;      // UI finished initialising
    private bool _loading;    // programmatic UI updates — don't write back to settings
    private bool _starting, _closed;
    private string _preset = "studio";

    private double _inDisp, _outDisp;

    public MainWindow()
    {
        InitializeComponent();
        LoadDevices();
        LoadConfig();
        _ready = true;
        ApplySettingsToUi();
        Closing += (_, _) => { _closed = true; StopProcessing(); SaveConfig(); };
    }

    // ───────────────────────── devices ─────────────────────────

    private void LoadDevices()
    {
        using var devices = new MMDeviceEnumerator();
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            InputCombo.Items.Add(new DeviceItem(device.ID, device.FriendlyName));
            device.Dispose();
        }
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            OutputCombo.Items.Add(new DeviceItem(device.ID, device.FriendlyName));
            device.Dispose();
        }

        if (InputCombo.Items.Count > 0) InputCombo.SelectedIndex = 0;

        var cable = OutputCombo.Items.Cast<DeviceItem>()
            .FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
        if (cable != null) OutputCombo.SelectedItem = cable;
        else if (OutputCombo.Items.Count > 0) OutputCombo.SelectedIndex = 0;

        if (cable == null)
            StatusText.Text = "لم أجد VB-Cable. ثبّته من vb-audio.com/Cable ثم أعد فتح البرنامج.";
    }

    // ───────────────────────── presets & parameters ─────────────────────────

    private void Preset_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loading) return;
        if (sender is not RadioButton { Tag: string key }) return;

        _preset = key;
        bool bypass = _settings.Bypass;
        _settings.CopyFrom(ProcessingSettings.FromPreset(key, _settings.Engine));
        _settings.Bypass = bypass;
        ApplySettingsToUi();
        _suppressor?.UpdateSettings(_settings);
    }

    private void Engine_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loading || _suppressor != null || _starting) return;
        if (EngineCombo.SelectedItem is not ComboBoxItem { Tag: string kind } ||
            !Enum.TryParse<DenoiserKind>(kind, out var engine)) return;
        _settings.Engine = engine;
        _settings.GateEnabled = engine == DenoiserKind.RNNoise;
        ApplySettingsToUi();
    }

    private void Param_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ReadUiToSettings();
    private void Param_Clicked(object sender, RoutedEventArgs e) => ReadUiToSettings();

    private void ApplySettingsToUi()
    {
        _loading = true;
        try
        {
            var s = _settings;
            StrengthSlider.Value = s.Strength * 100;
            GateSlider.Value = (s.GateThreshold - 0.15) / 0.70 * 100;
            GateDepthSlider.Value = s.GateDepthDb;
            EngineCombo.SelectedIndex = s.Engine == DenoiserKind.DeepFilterNet3 ? 0 : 1;
            GateEnabledCheck.IsChecked = s.GateEnabled;
            HighPassCheck.IsChecked = s.HighPassEnabled;
            GateSlider.IsEnabled = GateDepthSlider.IsEnabled = s.GateEnabled;
            InputGainSlider.Value = s.InputGainDb;
            PresenceSlider.Value = s.PresenceDb;
            MudSlider.Value = s.MudCutDb;
            CompCheck.IsChecked = s.CompressorOn;
            CompThresholdSlider.Value = s.CompThresholdDb;
            CompRatioSlider.Value = s.CompRatio;
            OutputGainSlider.Value = s.OutputGainDb;
            BypassCheck.IsChecked = s.Bypass;

            foreach (var rb in PresetRadios())
                rb.IsChecked = (string?)rb.Tag == _preset;

            PresetHint.Text = PresetHints.GetValueOrDefault(_preset, "");
        }
        finally { _loading = false; }
    }

    private void ReadUiToSettings()
    {
        if (!_ready || _loading) return;
        var s = _settings;
        s.Strength = (float)(StrengthSlider.Value / 100.0);
        s.GateThreshold = (float)(0.15 + GateSlider.Value / 100.0 * 0.70);
        s.GateDepthDb = (float)GateDepthSlider.Value;
        s.GateEnabled = GateEnabledCheck.IsChecked == true;
        s.HighPassEnabled = HighPassCheck.IsChecked == true;
        GateSlider.IsEnabled = GateDepthSlider.IsEnabled = s.GateEnabled;
        s.InputGainDb = (float)InputGainSlider.Value;
        s.PresenceDb = (float)PresenceSlider.Value;
        s.MudCutDb = (float)MudSlider.Value;
        s.CompressorOn = CompCheck.IsChecked == true;
        s.CompThresholdDb = (float)CompThresholdSlider.Value;
        s.CompRatio = (float)CompRatioSlider.Value;
        s.OutputGainDb = (float)OutputGainSlider.Value;
        s.Bypass = BypassCheck.IsChecked == true;
        _suppressor?.UpdateSettings(s);
    }

    private IEnumerable<RadioButton> PresetRadios() => PresetPanel.Children.OfType<RadioButton>();

    // ───────────────────────── start / stop ─────────────────────────

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_starting) return;
        if (_suppressor == null) await StartProcessing();
        else StopProcessing();
    }

    private async Task StartProcessing()
    {
        if (InputCombo.SelectedItem is not DeviceItem input ||
            OutputCombo.SelectedItem is not DeviceItem output)
        {
            StatusText.Text = "اختر المايك والمخرج أولًا.";
            return;
        }

        _starting = true;
        ToggleButton.IsEnabled = InputCombo.IsEnabled = OutputCombo.IsEnabled = EngineCombo.IsEnabled = false;
        StatusText.Text = "بيجهّز محرّك العزل…";
        try
        {
            ReadUiToSettings();
            var settings = _settings.Clone();
            var suppressor = await Task.Run(() => new NoiseSuppressor(input.Id, output.Id, settings));
            if (_closed) { suppressor.Dispose(); return; }
            _suppressor = suppressor;
            suppressor.UpdateSettings(_settings);
            suppressor.Meters += m => OnMeters(suppressor, m);
            suppressor.Failed += error => Dispatcher.BeginInvoke(() =>
            {
                if (_suppressor != suppressor) return;
                StopProcessing();
                StatusText.Text = "توقفت المعالجة: " + error.Message;
            });
            _suppressor.Start();

            ToggleButton.Content = "إيقاف";
            ToggleButton.Background = (Brush)FindResource("Danger");
            SetStatus(true);
            StatusText.Text = $"يعمل بـ{suppressor.EngineName}. اختار CABLE Output في برنامج المكالمات.";
            PerformanceText.Text = $"تأخير خوارزمي: {suppressor.AlgorithmicDelayMs:0}ms؛ تأخير الأجهزة والـbuffers إضافي.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "خطأ: " + ex.Message;
            _suppressor?.Dispose();
            _suppressor = null;
        }
        finally
        {
            _starting = false;
            ToggleButton.IsEnabled = true;
            InputCombo.IsEnabled = OutputCombo.IsEnabled = EngineCombo.IsEnabled = _suppressor == null;
        }
    }

    private void StopProcessing()
    {
        if (_suppressor == null) return;
        var suppressor = _suppressor;
        _suppressor = null;
        suppressor.Dispose();

        ToggleButton.Content = "ابدأ العزل";
        ToggleButton.Background = (Brush)FindResource("Accent");
        InputCombo.IsEnabled = OutputCombo.IsEnabled = EngineCombo.IsEnabled = true;
        _inDisp = _outDisp = 0;
        SetMask(InMask, InTrack, 0);
        SetMask(OutMask, OutTrack, 0);
        VoiceDot.Fill = new SolidColorBrush(Color.FromRgb(0xC5, 0xCB, 0xD6));
        VoiceText.Text = "في الانتظار";
        GateText.Text = "";
        PerformanceText.Text = "";
        SetStatus(false);
        StatusText.Text = "تم الإيقاف.";
    }

    private void SetStatus(bool running)
    {
        StatusPill.Text = running ? "يعمل" : "متوقف";
        StatusDot.Fill = new SolidColorBrush(running
            ? Color.FromRgb(0x12, 0xB5, 0xA0)
            : Color.FromRgb(0x6B, 0x72, 0x80));
    }

    // ───────────────────────── meters ─────────────────────────

    private void OnMeters(NoiseSuppressor source, MeterData m)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_suppressor != source) return;

            _inDisp = Math.Max(ToMeter(m.InPeak), _inDisp * 0.88);
            _outDisp = Math.Max(ToMeter(m.OutPeak), _outDisp * 0.88);
            SetMask(InMask, InTrack, _inDisp);
            SetMask(OutMask, OutTrack, _outDisp);
            PerformanceText.Text = $"أبطأ إطار: {m.FrameMs:0.0}ms / 10ms | صوت منتظر: {m.QueuedMs:0}ms | نقص بيانات الخرج: {m.Underruns}";

            if (m.Bypass)
            {
                VoiceDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24));
                VoiceText.Text = "المعالجة متجاوَزة (الصوت الأصلي)";
                GateText.Text = "";
                return;
            }

            if (m.Vad is null)
            {
                VoiceDot.Fill = new SolidColorBrush(Color.FromRgb(0xC5, 0xCB, 0xD6));
                VoiceText.Text = "بوابة الكلام متوقفة";
                GateText.Text = "";
                return;
            }
            bool voice = m.Vad.Value >= _settings.GateThreshold;
            VoiceDot.Fill = new SolidColorBrush(voice
                ? Color.FromRgb(0x12, 0xB5, 0xA0)
                : Color.FromRgb(0xC5, 0xCB, 0xD6));
            VoiceText.Text = voice ? "صوت بشري" : "صمت / ضوضاء";

            double gateDb = 20 * Math.Log10(Math.Max(m.GateGain, 0.0001f));
            GateText.Text = !_settings.GateEnabled ? "البوابة: متوقفة" :
                gateDb > -1 ? "البوابة: مفتوحة" : $"البوابة: {gateDb:0} dB";
        });
    }

    /// <summary>Linear peak (0..1) → meter position, -60 dBFS … 0 dBFS.</summary>
    private static double ToMeter(float lin)
    {
        if (lin <= 0.001f) return 0;
        double db = 20 * Math.Log10(lin);
        return Math.Clamp((db + 60) / 60, 0, 1);
    }

    private static void SetMask(Border mask, Border track, double level)
    {
        double w = track.ActualWidth;
        if (w <= 0) return;
        mask.Width = Math.Max(0, (1 - level) * w);
    }

    // ───────────────────────── config ─────────────────────────

    private sealed class AppConfig
    {
        public string? InputDevice { get; set; }
        public string? OutputDevice { get; set; }
        public string Preset { get; set; } = "studio";
        public ProcessingSettings? Settings { get; set; }
    }

    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
            if (cfg == null) return;

            _preset = cfg.Preset;
            if (cfg.Settings != null) _settings.CopyFrom(cfg.Settings);

            var inp = InputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Name == cfg.InputDevice);
            if (inp != null) InputCombo.SelectedItem = inp;
            var outp = OutputCombo.Items.Cast<DeviceItem>().FirstOrDefault(d => d.Name == cfg.OutputDevice);
            if (outp != null) OutputCombo.SelectedItem = outp;
        }
        catch
        {
            // a corrupt config must never stop the app from starting
        }
    }

    private void SaveConfig()
    {
        try
        {
            var cfg = new AppConfig
            {
                InputDevice = (InputCombo.SelectedItem as DeviceItem)?.Name,
                OutputDevice = (OutputCombo.SelectedItem as DeviceItem)?.Name,
                Preset = _preset,
                Settings = _settings
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best effort
        }
    }

    private sealed record DeviceItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
