using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.ReactiveUI;
using DWS.Core;
using DWS.Core.Json;
using DWS.Core.Logging;
using DWS.Core.Security;

namespace DWS.GUI;

public partial class Program
{
    private static readonly string DwsDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DWS");

    private static readonly string LogFile = Path.Combine(DwsDataDir, "gui_startup.log");
    private static readonly string AuditLogFile = Path.Combine(DwsDataDir, "audit_startup.log");

    private const int ATTACH_PARENT_PROCESS = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_ICONERROR = 0x00000010;

    static Program()
    {
        try
        {
            SecurePathGuard.EnsureSecureDirectory(DwsDataDir);
        }
        catch { }
    }

    public static void Log(string msg, bool isAudit = false)
    {
        string targetLog = isAudit ? AuditLogFile : LogFile;
        try
        {
            using var fs = new FileStream(targetLog, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(fs, Encoding.UTF8);
            writer.WriteLine($"[{DateTime.UtcNow:O}] {msg}");
        }
        catch { }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var tracePath = Path.Combine(DwsDataDir, "avalonia_trace.log");
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(tracePath));
            System.Diagnostics.Trace.AutoFlush = true;

            // Global execution lock to prevent race conditions between GUI and background Watchdog
            using var globalMutex = new Mutex(initiallyOwned: false, @"Global\DWS11_Enterprise_ExecutionLock", out bool createdNew);
            bool mutexAcquired = false;

            try
            {
                // Wait up to 10 seconds for concurrent runs to finish cleanly
                mutexAcquired = globalMutex.WaitOne(TimeSpan.FromSeconds(10), exitContext: false);
                if (!mutexAcquired)
                {
                    Log("WARN: Failed to acquire Global\\DWS11_Enterprise_ExecutionLock within 10s. Continuing with caution.");
                }

                Log("Program.Main started. Args: " + string.Join(" ", args ?? Array.Empty<string>()));
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Log($"UnhandledException: {e.ExceptionObject}");
                };

                // Check headless /SILENT-AUDIT argument
                if (args != null && Array.Exists(args, a => string.Equals(a, "/SILENT-AUDIT", StringComparison.OrdinalIgnoreCase)))
                {
                    // Attach to calling console (CMD/PowerShell) if available
                    AttachConsole(ATTACH_PARENT_PROCESS);

                    Console.WriteLine("DWS 11 Enterprise Watchdog - Running /SILENT-AUDIT evaluation...");
                    Log("Starting headless /SILENT-AUDIT execution...", isAudit: true);
                    int exitCode = RunSilentAudit();
                    Log($"/SILENT-AUDIT finished with exit code {exitCode}.", isAudit: true);
                    Console.WriteLine($"DWS 11 Enterprise /SILENT-AUDIT finished. Exit code: {exitCode}");
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
            finally
            {
                if (mutexAcquired)
                {
                    try { globalMutex.ReleaseMutex(); } catch { }
                }
            }
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
            SecurePathGuard.EnsureSecureDirectory(auditDir);

            var coordinator = new EnterprisePipelineCoordinator();
            var result = coordinator.ExecuteEnterpriseBaselineAsync(
                targetRuleIds: null,
                isDryRun: true,
                progress: null,
                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            if (result == null || result.AuditReport == null)
            {
                Log("SILENT-AUDIT Error: Failed to generate audit report.", isAudit: true);
                Console.WriteLine("Error: Failed to evaluate enterprise baseline audit report.");
                return 1;
            }

            var report = result.AuditReport;
            string reportJson = JsonSerializer.Serialize(report, DwsJsonContext.Default.AuditReport);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string reportPath = Path.Combine(auditDir, $"audit_report_{timestamp}.json");
            string latestPath = Path.Combine(auditDir, "latest_audit_report.json");

            File.WriteAllText(reportPath, reportJson, Encoding.UTF8);
            File.WriteAllText(latestPath, reportJson, Encoding.UTF8);

            string summary = $"Evaluated: {report.TotalRulesEvaluated}, Non-compliant: {report.PendingChangesCount}, Compliant: {report.CompliantRulesCount}. Report: {reportPath}";
            Log($"SILENT-AUDIT completed. {summary}", isAudit: true);
            Console.WriteLine($"Audit complete: {summary}");

            return report.PendingChangesCount > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Log($"SILENT-AUDIT Exception: {ex}", isAudit: true);
            Console.WriteLine($"Audit Exception: {ex.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
}
