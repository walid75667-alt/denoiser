using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using NAudio.Wave;

namespace MicDenoiser;

public partial class MainWindow
{
    private AudioHistory? _lastHistory;
    private FrameTimingSnapshot _lastTiming;
    private async void SaveHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_starting || _abBusy || _comparison != null) return;
        // Stop before copying the worker-owned ring. History remains available after a device fault.
        if (_suppressor != null) StopProcessing();
        var history = _lastHistory;
        if (history == null || history.Original.Length == 0) { HistoryStatus.Text = T("HistoryEmpty"); return; }
        var dialog = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", DefaultExt = ".zip", FileName = "MicDenoiser-audio-diagnostics.zip" };
        if (dialog.ShowDialog(this) != true) return;
        var timing = _lastTiming; var evidence = _lastDiagnostic;
        var settings = (_diagnosticSettings ?? _settings).SanitizedClone();
        SaveHistoryButton.IsEnabled = false;
        try
        {
            // Write into a new temporary file, then replace only the file the user selected.
            await Task.Run(() =>
            {
                string temporary = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Guid.NewGuid() + ".tmp");
                try
                {
                    using (var zip = System.IO.Compression.ZipFile.Open(temporary, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        void Audio(string name, float[] samples)
                        {
                            var entry = zip.CreateEntry(name);
                            using var stream = entry.Open();
                            // WaveFileWriter needs a seekable stream; this runs after audio has stopped.
                            using var memory = new MemoryStream();
                            using (var wav = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(memory), WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
                                foreach (float sample in samples) wav.WriteSample(sample / 32768);
                            memory.Position = 0; memory.CopyTo(stream);
                        }
                        Audio("original.wav", history.Original); Audio("processed.wav", history.Processed);
                        using var report = new StreamWriter(zip.CreateEntry("report.json").Open());
                        report.Write(JsonSerializer.Serialize(new { Version = "2.5", CreatedUtc = DateTime.UtcNow,
                            history.Seconds, Timing = timing, Evidence = evidence, Settings = settings,
                            SettingsScope = "Most recent observed settings; the history can span earlier live parameter changes.",
                            Capture = "Delay-aligned original and DSP output; excludes WASAPI playback/device artifacts. Raw levels, no loudness matching." },
                            new JsonSerializerOptions { WriteIndented = true }));
                    }
                    File.Move(temporary, dialog.FileName, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            });
            HistoryStatus.Text = T("HistorySaved", history.Seconds);
        }
        catch (Exception ex) { HistoryStatus.Text = T("Error", UiStrings.ErrorDetail(ex.Message)); }
        finally { if (!_closed) SaveHistoryButton.IsEnabled = true; }
    }
}
