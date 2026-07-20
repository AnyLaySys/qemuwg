using Microsoft.UI.Xaml;

namespace QemuWG;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        AppLog.Write("App constructor");
        InitializeComponent();
        UnhandledException += (_, args) => AppLog.Write("Unhandled exception: " + args.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Write("OnLaunched");
        window = new MainWindow();
        AppLog.Write("MainWindow constructed");
        window.Activate();
        AppLog.Write("MainWindow activated");
    }
}

internal static class AppLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "qemuwg",
        "app.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
