using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.ReactiveUI;

namespace DWS.GUI;

public partial class Program
{
    private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "gui_startup.log");

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_ICONERROR = 0x00000010;

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

            // Pre-flight check: Required unmanaged native rendering libraries for Avalonia
            if (OperatingSystem.IsWindows())
            {
                string skiaDll = Path.Combine(AppContext.BaseDirectory, "libSkiaSharp.dll");
                if (!File.Exists(skiaDll))
                {
                    string msg = "Error crítico de componentes:\n\n" +
                                 "No se encontró la biblioteca gráfica nativa 'libSkiaSharp.dll' en el directorio de la aplicación:\n" +
                                 AppContext.BaseDirectory + "\n\n" +
                                 "Por favor, asegúrese de extraer TODO el contenido del archivo ZIP de la versión (incluyendo las DLLs nativas) en la misma carpeta antes de iniciar DWS.GUI.exe.";
                    Log($"FATAL: Missing native library: {skiaDll}");
                    try { MessageBox(IntPtr.Zero, msg, "DWS 11 Enterprise - Componente Faltante", MB_ICONERROR); } catch { }
                    Environment.Exit(1);
                }
            }

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
