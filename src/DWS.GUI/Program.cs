using Avalonia;
using Avalonia.ReactiveUI;

namespace DWS.GUI;

public class Program
{
    private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "gui_startup.log");

    public static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.UtcNow:O}] {msg}\n"); } catch { }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var tracePath = Path.Combine(AppContext.BaseDirectory, "avalonia_trace.log");
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(tracePath));
            System.Diagnostics.Trace.AutoFlush = true;

            Log("Program.Main started.");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log($"UnhandledException: {e.ExceptionObject}");
            };

            // OS verification: minimum Windows 10 22H2 (build 19045)
            var osVersion = Environment.OSVersion.Version;
            if (osVersion.Build < 19045)
            {
                Log($"OS unsupported build: {osVersion.Build}");
                Environment.Exit(1);
            }

            Log("Configuring Avalonia app...");
            var app = BuildAvaloniaApp();
            Log("Starting ClassicDesktopLifetime...");
            app.StartWithClassicDesktopLifetime(args);
            Log("StartWithClassicDesktopLifetime finished normally.");
        }
        catch (Exception ex)
        {
            Log($"FATAL in Main: {ex}");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
}
