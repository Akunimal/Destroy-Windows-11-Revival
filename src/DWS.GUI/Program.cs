using Avalonia;
using Avalonia.ReactiveUI;

namespace DWS.GUI;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // OS verification: minimum Windows 10 22H2 (build 19045)
        var osVersion = Environment.OSVersion.Version;
        if (osVersion.Build < 19045)
        {
            Console.WriteLine("DWS 11 Enterprise v2.0 requires Windows 10 22H2 (build 19045) or higher.");
            Environment.Exit(1);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
}
