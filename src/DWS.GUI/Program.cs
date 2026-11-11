using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.ReactiveUI;
using DWS.Core;
using DWS.Core.Json;
using DWS.Core.Logging;

namespace DWS.GUI;

public partial class Program
{
    private static readonly string DwsDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DWS");

    private static readonly string LogFile = Path.Combine(DwsDataDir, "gui_startup.log");

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_ICONERROR = 0x00000010;

    static Program()
    {
        try { Directory.CreateDirectory(DwsDataDir); } catch { }
    }

    public static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.UtcNow:O}] {msg}\n"); } catch { }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var tracePath = Path.Combine(DwsDataDir, "avalonia_trace.log");
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(tracePath));
            System.Diagnostics.Trace.AutoFlush = true;

            Log("Program.Main started. Args: " + string.Join(" ", args));
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log($"UnhandledException: {e.ExceptionObject}");
            };

            // Check headless /SILENT-AUDIT argument
            if (args != null && Array.Exists(args, a => string.Equals(a, "/SILENT-AUDIT", StringComparison.OrdinalIgnoreCase)))
            {
                Log("Starting headless /SILENT-AUDIT execution...");
                int exitCode = RunSilentAudit();
                Log($"/SILENT-AUDIT finished with exit code {exitCode}.");
                Environment.Exit(exitCode);
                return;
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
            app.StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());
            Log("StartWithClassicDesktopLifetime finished normally.");
        }
        catch (Exception ex)
        {
            Log($"FATAL in Main: {ex}");
            throw;
        }
    }

    public static int RunSilentAudit()
    {
        try
        {
            string auditDir = Path.Combine(DwsDataDir, "audit");
            Directory.CreateDirectory(auditDir);

            var coordinator = new EnterprisePipelineCoordinator();
            var result = coordinator.ExecuteEnterpriseBaselineAsync(
                targetRuleIds: null,
                isDryRun: true,
                progress: null,
                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            if (result == null || result.AuditReport == null)
            {
                Log("SILENT-AUDIT Error: Failed to generate audit report.");
                return 1;
            }

            var report = result.AuditReport;
            string reportJson = JsonSerializer.Serialize(report, DwsJsonContext.Default.AuditReport);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string reportPath = Path.Combine(auditDir, $"audit_report_{timestamp}.json");
            string latestPath = Path.Combine(auditDir, "latest_audit_report.json");

            File.WriteAllText(reportPath, reportJson, Encoding.UTF8);
            File.WriteAllText(latestPath, reportJson, Encoding.UTF8);

            Log($"SILENT-AUDIT completed. Evaluated: {report.TotalRulesEvaluated}, Non-compliant / Pending: {report.PendingChangesCount}, Compliant: {report.CompliantRulesCount}. Report: {reportPath}");

            return report.PendingChangesCount > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Log($"SILENT-AUDIT Exception: {ex}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
}
