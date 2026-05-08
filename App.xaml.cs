using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SteamPlaylogViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Log(args.Exception);
            MessageBox.Show($"Unhandled exception:\n{args.Exception.Message}\n\nLog: {Logger.LogPath}",
                "SteamPlaylogViewer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Logger.Log(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Log(args.Exception);
            args.SetObserved();
        };
    }
}

internal static class Logger
{
    public static string LogPath { get; } =
        Path.Combine(Path.GetTempPath(), "SteamPlaylogViewer.log");

    public static void Log(Exception ex)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n{ex}\n\n");
        }
        catch { }
    }
}
