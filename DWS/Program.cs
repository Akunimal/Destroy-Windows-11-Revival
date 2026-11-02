using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DWS.Core;
using DWS.Core.Logging;
using DWS.Core.Security;

namespace DWS_Lite
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length >= 2 && string.Equals(args[0], "/WORKER", StringComparison.OrdinalIgnoreCase))
            {
                // Headless privileged SYSTEM worker mode executed via Named Pipe IPC
                SystemPrivilegeBroker.RunWorkerClient(args[1], command =>
                {
                    TransactionJournal.Log(JournalEntryType.SecurityOperation, $"SYSTEM worker processed IPC command: {command}");
                    return "OK:SYSTEM_PROCESSED";
                });
                return;
            }

            if (args != null && Array.Exists(args, a => string.Equals(a, "/SILENT-AUDIT", StringComparison.OrdinalIgnoreCase)))
            {
                // Headless background watchdog audit mode triggered after Windows Updates
                int exitCode = RunSilentAudit();
                Environment.Exit(exitCode);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Opt into dark mode so native scroll bars render dark (Windows 11).
            DWS_Lite.lib.Win11Visual.EnableDarkModeForApp();
            Application.Run(new MainDwsForm(args));
        }

        private static int RunSilentAudit()
        {
            try
            {
                TransactionJournal.Log(JournalEntryType.Audit, "Executing Headless /SILENT-AUDIT triggered by Windows Update Watchdog...");
                var coordinator = new EnterprisePipelineCoordinator();
                var result = coordinator.ExecuteEnterpriseBaselineAsync(null, true, CancellationToken.None, null).Result;

                if (result == null || result.AuditReport == null)
                {
                    LogToWindowsEventLog("DWS Enterprise Audit Error: Failed to generate audit report.", EventLogEntryType.Error, 1003);
                    return 1;
                }

                var report = result.AuditReport;
                int regressions = report.PendingChangesCount;

                if (regressions > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"DWS Enterprise Baseline Regression Detected: {regressions} policy rules reverted or non-compliant after Windows Update.");
                    sb.AppendLine($"Evaluation Timestamp: {report.TimestampUtc:u}");
                    sb.AppendLine($"Overall Risk Level: {report.OverallRiskLevel}");
                    sb.AppendLine();
                    sb.AppendLine("Reverted Policies / Pending Non-Compliant Keys:");
                    foreach (var change in report.PendingChanges)
                    {
                        sb.AppendLine($" - [{change.ActionType}] {change.RuleName} ({change.Hive}\\{change.KeyPath}\\{change.ValueName}) -> Expected: {change.DesiredValue}, Current: {change.CurrentValue}");
                    }

                    string warningMsg = sb.ToString();
                    TransactionJournal.Log(JournalEntryType.Warning, warningMsg);
                    LogToWindowsEventLog(warningMsg, EventLogEntryType.Warning, 1001);
                    return 2;
                }
                else
                {
                    string okMsg = $"DWS Enterprise Baseline Verified: All {report.CompliantRulesCount} policies and transacted registry states remain intact and compliant after Windows Update.";
                    TransactionJournal.Log(JournalEntryType.Info, okMsg);
                    LogToWindowsEventLog(okMsg, EventLogEntryType.Information, 1000);
                    return 0;
                }
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, "Fatal exception during /SILENT-AUDIT execution", ex);
                LogToWindowsEventLog("DWS Enterprise Audit Fatal Exception: " + ex.ToString(), EventLogEntryType.Error, 1002);
                return 1;
            }
        }

        private static void LogToWindowsEventLog(string message, EventLogEntryType entryType, int eventId)
        {
            const string source = "DWS Enterprise";
            const string logName = "Application";

            try
            {
                if (!EventLog.SourceExists(source))
                {
                    EventLog.CreateEventSource(source, logName);
                }

                EventLog.WriteEntry(source, message, entryType, eventId);
            }
            catch (Exception ex)
            {
                // In unprivileged or restricted environments, EventLog registration may fail; journal already captures everything
                TransactionJournal.Log(JournalEntryType.Error, "Unable to register or write to Windows EventLog", ex);
            }
        }
    }
}
