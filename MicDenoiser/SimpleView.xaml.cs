using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MicDenoiser;

public partial class SimpleView : UserControl
{
    private bool _loading, _ready;
    public event Action? Power, Mute, Studio, Settings, Diagnosis, Setup;
    public event Action<bool>? Compare;
    public event Action<string>? Level;
    public event Action<string, string>? Devices;
    public SimpleView() { InitializeComponent(); _ready = true; }
    public sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
    public void SetDevices(IEnumerable<Choice> microphones, IEnumerable<Choice> outputs, string? microphone, string? output, bool enabled)
    {
        _loading = true;
        try
        {
            MicCombo.ItemsSource = microphones; OutputCombo.ItemsSource = outputs;
            MicCombo.SelectedItem = microphones.FirstOrDefault(d => d.Id == microphone);
            OutputCombo.SelectedItem = outputs.FirstOrDefault(d => d.Id == output);
            MicCombo.IsEnabled = OutputCombo.IsEnabled = enabled;
            DeviceSummary.Text = $"{MicCombo.SelectedItem} → {OutputCombo.SelectedItem}";
        }
        finally { _loading = false; }
    }
    public void Refresh(bool running, bool starting, ProcessingSettings settings, string level, string status)
    {
        _loading = true;
        try
        {
            LightLevel.IsChecked = level == "light"; BalancedLevel.IsChecked = level == "balanced"; StrongLevel.IsChecked = level == "strong";
            LightLevel.IsEnabled = BalancedLevel.IsEnabled = StrongLevel.IsEnabled = !starting;
            LevelHint.Text = UiStrings.Get("LevelHint" + level);
            Headline.Text = UiStrings.Get(starting ? "SwitchingEngine" : !running ? "Ui001" : settings.Muted ? "MuteStatus" : settings.Bypass ? "OriginalActive" : "Running");
            Subline.Text = UiStrings.Get(!running ? "CompactIdle" : settings.Muted ? "CompactMuted" : settings.Bypass ? "HoldRelease" : "CompactRunning");
            MuteButton.Content = UiStrings.Get(settings.Muted ? "Unmute" : "Mute");
            MuteButton.IsEnabled = !starting;
            PowerButton.IsEnabled = !starting;
            PowerButton.Opacity = running ? 1 : .55;
            CompareButton.IsEnabled = running && !starting && !settings.Muted;
            CompareButton.Background = (Brush)FindResource(settings.Bypass ? "WarningSoft" : "CardBg");
            StatusText.Text = status;
            if (!running || settings.Muted || settings.Bypass) ReductionText.Text = "— dB";
            if (!running) { InputMeter.Value = OutputMeter.Value = 0; VoiceStatus.Text = UiStrings.Get("Ui001"); }
        }
        finally { _loading = false; }
    }
    public void RenderMeters(MeterData meters, ProcessingSettings settings)
    {
        static double Display(float rms) => rms <= .001f ? 0 : Math.Clamp((20 * Math.Log10(rms) + 60) / 60, 0, 1);
        InputMeter.Value = Display(meters.InRms); OutputMeter.Value = Display(meters.OutRms);
        ReductionText.Text = meters.BackgroundReductionDb is { } reduction ? $"{reduction:0.0} dB" : "— dB";
        VoiceStatus.Text = UiStrings.Get(settings.Muted ? "MuteStatus" : settings.Bypass ? "OriginalActive" : meters.Vad == null ? "VadUnavailable" : meters.Vad >= .35f ? "Voice" : "LowVoiceEvidence");
    }
    private void Power_Click(object sender, RoutedEventArgs e) => Power?.Invoke();
    private void Mute_Click(object sender, RoutedEventArgs e) => Mute?.Invoke();
    private void Studio_Click(object sender, RoutedEventArgs e) => Studio?.Invoke();
    private void Settings_Click(object sender, RoutedEventArgs e) => Settings?.Invoke();
    private void Diagnosis_Click(object sender, RoutedEventArgs e) => Diagnosis?.Invoke();
    private void Setup_Click(object sender, RoutedEventArgs e) => Setup?.Invoke();
    private void Level_Checked(object sender, RoutedEventArgs e) { if (_ready && !_loading && sender is RadioButton { Tag: string level }) Level?.Invoke(level); }
    private void Devices_Changed(object sender, SelectionChangedEventArgs e)
    { if (_ready && !_loading && MicCombo.SelectedItem is Choice mic && OutputCombo.SelectedItem is Choice output) Devices?.Invoke(mic.Id, output.Id); }
    private void Compare_Down(object sender, MouseButtonEventArgs e) { CompareButton.CaptureMouse(); Compare?.Invoke(true); e.Handled = true; }
    private void Compare_Up(object sender, MouseButtonEventArgs e) { Compare?.Invoke(false); CompareButton.ReleaseMouseCapture(); e.Handled = true; }
    private void Compare_Lost(object sender, MouseEventArgs e) => Compare?.Invoke(false);
    private void Compare_Blur(object sender, KeyboardFocusChangedEventArgs e) => Compare?.Invoke(false);
    private void Compare_KeyDown(object sender, KeyEventArgs e) { if (e.Key is Key.Space or Key.Enter) { if (!e.IsRepeat) Compare?.Invoke(true); e.Handled = true; } }
    private void Compare_KeyUp(object sender, KeyEventArgs e) { if (e.Key is Key.Space or Key.Enter) { Compare?.Invoke(false); e.Handled = true; } }
}
