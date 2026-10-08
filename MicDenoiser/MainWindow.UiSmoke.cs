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
            if (window == this)
            {
                var area = CurrentWorkArea();
                if (Left < area.Left - 2 || Top < area.Top - 2 || Left + ActualWidth > area.Right + 2 || Top + ActualHeight > area.Bottom + 2)
                    throw new InvalidOperationException("Mode switch places the window outside the working area.");
            }
            var content = (FrameworkElement)window.Content;
            var margin = content.Margin;
            int width = (int)Math.Ceiling(content.ActualWidth + margin.Left + margin.Right), height = (int)Math.Ceiling(content.ActualHeight + margin.Top + margin.Bottom);
            if (width < 300 || height < 300) throw new InvalidOperationException("Window did not lay out.");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle((Brush)FindResource("Surface"), null, new Rect(0, 0, width, height));
                // A detached RTL visual retains its layout mirror; restore the window's coordinate direction.
                if (window.FlowDirection == FlowDirection.RightToLeft)
                    context.PushTransform(new MatrixTransform(-1, 0, 0, 1, width, 0));
                context.DrawRectangle(new VisualBrush(content), null, new Rect(margin.Left, margin.Top, content.ActualWidth, content.ActualHeight));
                if (window.FlowDirection == FlowDirection.RightToLeft) context.Pop();
            }
            bitmap.Render(drawing);
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
