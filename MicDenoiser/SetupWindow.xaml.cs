using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace MicDenoiser;
public partial class SetupWindow : Window
{
    private sealed record Device(string Id, string Name) { public override string ToString() => Name; }
    private readonly ProcessingSettings _settings;
    private readonly CancellationTokenSource _cancel = new();
    private WasapiCapture? _capture; private MMDevice? _microphone, _headphones;
    private WasapiOut? _player;
    private ComparisonResult? _result;
    private readonly object _audioLock = new();
    private float[]? _recording; private int _recorded, _step = 1;
    private bool _ready, _busy, _closed, _smoke;
    public string? InputId => (MicrophoneCombo.SelectedItem as Device)?.Id;
    public string? OutputId => (RoutingCombo.SelectedItem as Device)?.Id;
    public string Purpose => PurposeCombo.SelectedIndex == 1 ? "studio" : "calls";
    public SetupWindow(string language, ProcessingSettings settings, string? input, string? output, string purpose)
    {
        InitializeComponent(); UiStrings.Apply(this, language); _settings = settings.SanitizedClone();
        _settings.Bypass = _settings.Muted = false;
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
        PurposeCombo.SelectedIndex = purpose == "studio" ? 1 : 0;
        LoadDevices(input, output); Detect(); _ready = true; RenderStep();
        Closing += (_, _) => { _closed = true; _cancel.Cancel(); StopCapture(); StopPlayback(); };
    }
    private void LoadDevices(string? input, string? output)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            Device[] List(DataFlow flow) => enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).Select(d => { using (d) return new Device(d.ID, d.FriendlyName); }).ToArray();
            var microphones = List(DataFlow.Capture); var outputs = List(DataFlow.Render);
            MicrophoneCombo.ItemsSource = microphones;
            MicrophoneCombo.SelectedItem = microphones.FirstOrDefault(d => d.Id == input) ?? microphones.FirstOrDefault(d => !d.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase));
            RoutingCombo.ItemsSource = outputs;
            RoutingCombo.SelectedItem = outputs.FirstOrDefault(d => d.Id == output) ?? outputs.FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)) ?? outputs.FirstOrDefault();
            var local = outputs.Where(d => !d.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)).ToArray();
            HeadphonesCombo.ItemsSource = local; HeadphonesCombo.SelectedItem = local.FirstOrDefault();
        }
        catch (Exception ex) { Error(ex); }
    }
    private void Detect()
    {
        try
        {
            using var devices = new MMDeviceEnumerator(); bool input = false, output = false;
            foreach (var endpoint in devices.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
                using (endpoint)
                {
                    input |= endpoint.DataFlow == DataFlow.Render && endpoint.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase);
                    output |= endpoint.DataFlow == DataFlow.Capture && endpoint.FriendlyName.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase);
                }
            CableStatus.Text = UiStrings.Get(input && output ? "SetupFound" : "SetupMissing");
        }
        catch (Exception ex) { Error(ex); }
    }
    internal void PrepareSmokeStep(int step) { _smoke = true; _step = step; RenderStep(); }
    private void RenderStep()
    {
        StepOne.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        StepTwo.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepThree.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        StepProgress.Value = _step; BackButton.IsEnabled = _step > 1 && !_busy;
        NextButton.Content = UiStrings.Get(_step == 3 ? "SetupDone" : "Next");
        NextButton.IsEnabled = !_busy; RecordButton.IsEnabled = !_busy && InputId != null;
        if (!_smoke && (_step == 2 || _step == 3)) StartCapture(); else StopCapture();
    }
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_step == 2 && (InputId == null || OutputId == null)) { StatusText.Text = UiStrings.Get("ChooseDevices"); return; }
        if (_step < 3) { ++_step; RenderStep(); } else DialogResult = true;
    }
    private void Back_Click(object sender, RoutedEventArgs e) { if (_busy || _step < 2) return; --_step; StopPlayback(); RenderStep(); }
    private void Microphone_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && !_busy) { StopCapture(); _result = null; OriginalButton.IsEnabled = ProcessedButton.IsEnabled = false; StartCapture(); } }
    private void StartCapture()
    {
        if (_smoke || _step == 1 || _capture != null || InputId == null || _closed) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator(); _microphone = enumerator.GetDevice(InputId);
            var capture = new WasapiCapture(_microphone); _capture = capture;
            var queued = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromSeconds(2), ReadFully = false };
            var mono = new DownmixSampleProvider(queued.ToSampleProvider());
            var samples = new StreamingResamplingSampleProvider(mono, () => queued.BufferedBytes / capture.WaveFormat.BlockAlign, 48000);
            var block = new float[4800];
            capture.DataAvailable += (_, data) =>
            {
                if (_closed || _capture != capture) return;
                try
                {
                    queued.AddSamples(data.Buffer, 0, data.BytesRecorded);
                    float peak = 0; bool complete = false;
                    for (int chunk = 0; chunk < 16; chunk++)
                    {
                        int count = samples.Read(block, 0, block.Length); if (count == 0) break;
                        for (int i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs(block[i]));
                        lock (_audioLock)
                        {
                            if (_recording != null)
                            {
                                int n = Math.Min(count, _recording.Length - _recorded);
                                for (int i = 0; i < n; i++) _recording[_recorded + i] = block[i] * 32768;
                                _recorded += n; complete |= _recorded == _recording.Length;
                            }
                        }
                        if (count < block.Length) break;
                    }
                    int recorded; lock (_audioLock) recorded = _recorded;
                    if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() =>
                    {
                        if (_closed || _capture != capture) return;
                        MicMeter.Value = peak <= .001f ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
                        MicLevel.Text = peak <= .001f ? "≤ -60 dBFS" : $"{20 * Math.Log10(peak):0.0} dBFS";
                        if (_busy) { RecordingProgress.Value = recorded / 48000.0; StatusText.Text = UiStrings.Get("RecordProgress", recorded / 48000.0); }
                        if (complete) CompleteRecording();
                    });
                }
                catch (Exception ex) { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { if (!_closed && _capture == capture) { AbortRecording(); Error(ex); } }); }
            };
            capture.RecordingStopped += (_, e) => { if (e.Exception != null && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { if (!_closed && _capture == capture) { AbortRecording(); Error(e.Exception); } }); };
            capture.StartRecording();
        }
        catch (Exception ex) { StopCapture(); Error(ex); }
    }
    private void StopCapture()
    {
        var capture = _capture; _capture = null;
        try { capture?.StopRecording(); } finally { capture?.Dispose(); _microphone?.Dispose(); _microphone = null; }
    }
    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _capture == null) return;
        _settings.CopyFrom(Purpose == "studio" ? ProcessingSettings.FromPreset("voiceover", _settings.Engine) : SuppressionLevel.Create("balanced", _settings));
        _settings.Bypass = _settings.Muted = false;
        StopPlayback(); _result = null; _busy = true;
        lock (_audioLock) { _recording = new float[480000]; _recorded = 0; }
        OriginalButton.IsEnabled = ProcessedButton.IsEnabled = false;
        RecordButton.IsEnabled = NextButton.IsEnabled = BackButton.IsEnabled = false;
        StatusText.Text = UiStrings.Get("RecordProgress", 0);
    }
    private async void CompleteRecording()
    {
        float[]? source; lock (_audioLock) { source = _recording; _recording = null; }
        if (source == null || _closed) return;
        StopCapture(); StatusText.Text = UiStrings.Get("RecordMatching");
        try
        {
            var raw = _settings.SanitizedClone(); raw.FastSinging = true; raw.Strength = 0; raw.GateEnabled = raw.EqEnabled = raw.CompressorOn = raw.DeEsserEnabled = raw.ReverbEnabled = raw.EchoEnabled = raw.ChorusEnabled = false;
            var rendered = await Task.Run(() => AbComparison.Render(source, raw, _settings, _cancel.Token));
            if (!_closed) { _result = ComparisonResult.Create(source, rendered.Processed); StatusText.Text = UiStrings.Get("RecordReady"); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) Error(ex); }
        finally { if (!_closed) { _busy = false; RenderStep(); OriginalButton.IsEnabled = ProcessedButton.IsEnabled = _result != null; } }
    }
    private void AbortRecording()
    {
        lock (_audioLock) _recording = null;
        _busy = false; StopCapture(); RecordButton.IsEnabled = false; NextButton.IsEnabled = true; BackButton.IsEnabled = true;
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null || _busy || HeadphonesCombo.SelectedItem is not Device output || sender is not Button { Tag: string clip }) return;
        StopPlayback();
        try
        {
            using var enumerator = new MMDeviceEnumerator(); _headphones = enumerator.GetDevice(output.Id);
            _player = new WasapiOut(_headphones, AudioClientShareMode.Shared, true, 100);
            _player.Init(_result.CreatePlayback(clip == "original")); _player.Play();
        }
        catch (Exception ex) { StopPlayback(); Error(ex); }
    }
    private void StopPlayback() { var player = _player; _player = null; try { player?.Stop(); } finally { player?.Dispose(); _headphones?.Dispose(); _headphones = null; } }
    private void StopPlay_Click(object sender, RoutedEventArgs e) => StopPlayback();
    private void Error(Exception ex) => StatusText.Text = UiStrings.Get("Error", UiStrings.ErrorDetail(ex.Message));
    private void Vendor_Click(object sender, RoutedEventArgs e)
    { try { Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true }); } catch (Exception ex) { Error(ex); } }
    private void Detect_Click(object sender, RoutedEventArgs e) { LoadDevices(InputId, OutputId); Detect(); }
}
