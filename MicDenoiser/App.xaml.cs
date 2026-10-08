using System.IO;
using System.Windows;
namespace MicDenoiser;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        bool smoke = e.Args.Contains("--ui-smoke");
        if (!smoke) StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
        base.OnStartup(e);
        if (!smoke) return;
        string directory = Path.GetFullPath(Path.Combine("artifacts", "ui-smoke"));
        try
        {
            var window = new MainWindow(); MainWindow = window; window.Show();
            await window.RenderUiSmoke(directory); Shutdown(0);
        }
        catch (Exception ex)
        { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString()); Shutdown(1); }
    }
}
