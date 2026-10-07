using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace MicDenoiser;
public partial class MainWindow
{
    // Explicit CI mode: render actual WPF controls without starting capture or playback.
    internal async Task RenderUiSmoke(string directory)
    {
        Directory.CreateDirectory(directory);
        async Task Capture(Window window, string name)
        {
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            int width = (int)Math.Ceiling(window.ActualWidth), height = (int)Math.Ceiling(window.ActualHeight);
            if (width < 300 || height < 300) throw new InvalidOperationException("Window did not lay out.");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
        }
        foreach (string language in new[] { "ar", "en" })
        {
            _language = language; UiStrings.Apply(this, language);
            _simpleMode = true; ApplySettingsToUi(); await Capture(this, language + "-simple");
            _simpleMode = false; _usagePurpose = "studio"; ApplySettingsToUi();
            for (int page = 0; page < MainTabs.Items.Count; page++)
            { MainTabs.SelectedIndex = page; await Capture(this, language + "-studio-" + page); }
            var wizard = new SetupWindow(language, _settings, null, null, "calls") { Owner = this };
            wizard.Show();
            for (int step = 1; step <= 3; step++) { wizard.PrepareSmokeStep(step); await Capture(wizard, language + "-setup-" + step); }
            wizard.Close();
            var about = new AboutWindow(language) { Owner = this }; about.Show(); await Capture(about, language + "-about"); about.Close();
        }
        _darkTheme = true; AppTheme.Apply(this, true); _simpleMode = true; ApplySettingsToUi(); await Capture(this, "en-simple-dark");
    }
}
