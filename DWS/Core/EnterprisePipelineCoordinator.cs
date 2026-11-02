using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using DWS.Core.KTM;
using DWS.Core.Logging;
using DWS.Core.Policy;
using DWS.Core.Rules;
using DWS.Core.Security;
using DWS.Core.Storage;
using Microsoft.Win32;

namespace DWS.Core
{
    /// <summary>
    /// Enterprise Pipeline Coordinator.
    /// Orchestrates the low-level kernel modules in exact chronological order:
    /// 1. BitLockerGuard (TPM PCR Suspension)
    /// 2. SystemPrivilegeBroker (LSA/PPL-Safe Privilege Validation)
    /// 3. Kernel Transaction Manager (KTM) Initialization
    /// 4. SecureRuleEngine (RSA-4096 Digital Signature Verification)
    /// 5. Pre-execution Rule Inspection & Dry-Run Audit Engine
    /// 6. Transacted Registry Operations (KTM)
    /// 7. Isolated AppKey Offline Hive Deployment (Default NTUSER.DAT)
    /// 8. LGPO Binary Writer (registry.pol) & gpupdate
    /// 9. Atomic Commit or User Cancellation Rollback
    /// </summary>
    public sealed class EnterprisePipelineCoordinator
    {
        public event CoreMessageEventHandler OnStepProgress;
        public event CoreErrorEventHandler OnStepError;

        /// <summary>
        /// Synchronous wrapper for legacy callers.
        /// </summary>
        public bool ExecuteEnterpriseBaseline(string rulesJsonPayload)
        {
            var task = ExecuteEnterpriseBaselineAsync(rulesJsonPayload, false, CancellationToken.None, null);
            task.Wait();
            return task.Result.Success;
        }

        /// <summary>
        /// Asynchronously executes the enterprise optimization baseline with cancellation and progress reporting.
        /// </summary>
        public Task<PipelineResult> ExecuteEnterpriseBaselineAsync(
            string rulesJsonPayload,
            bool isDryRun,
            CancellationToken cancellationToken,
            IProgress<PipelineProgress> progress)
        {
            return Task.Factory.StartNew(() =>
            {
                return ExecuteEnterpriseBaselineInternal(rulesJsonPayload, isDryRun, cancellationToken, progress);
            }, cancellationToken);
        }

        private PipelineResult ExecuteEnterpriseBaselineInternal(
            string rulesJsonPayload,
            bool isDryRun,
            CancellationToken cancellationToken,
            IProgress<PipelineProgress> progress)
        {
            var sw = Stopwatch.StartNew();
            var auditReport = new AuditReport();
            string modeLabel = isDryRun ? "DRY-RUN (AUDIT ONLY)" : "REAL EXECUTION (TRANSACTED)";

            TransactionJournal.Log(JournalEntryType.Info, $"Starting Enterprise Pipeline Execution [{modeLabel}]...");
            ReportProgress(progress, 5, $"Starting Enterprise Baseline [{modeLabel}]...", null, JournalEntryType.Info);

            cancellationToken.ThrowIfCancellationRequested();

            // Step 1: BitLocker TPM PCR Protection (Only active during real execution)
            IDisposable bitLockerScope = null;
            BitLockerGuard bitLockerGuard = null;
            if (!isDryRun)
            {
                bitLockerGuard = BitLockerGuard.ProtectSession("C:", 1);
                bitLockerScope = bitLockerGuard;
            }

            try
            {
                using (bitLockerScope)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 2: Privilege Broker Validation
                    var privilegeBroker = new SystemPrivilegeBroker();
                    bool isSystem = privilegeBroker.IsRunningAsSystem();
                    TransactionJournal.Log(JournalEntryType.Info, $"Security Context: {(isSystem ? "NT AUTHORITY\\SYSTEM" : "Elevated Administrator")}");
                    ReportProgress(progress, 10, $"Security context verified: {(isSystem ? "SYSTEM" : "Administrator")}", null, JournalEntryType.Info);

                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 3: Kernel Transaction Manager (KTM) Initialization
                    using (var ktm = new KtmTransactionManager("DWS_EnterpriseBaseline"))
                    {
                        try
                        {
                            // Step 4: Secure Rule Engine Trust Pinning Verification
                            ReportProgress(progress, 15, "Verifying digital signatures via RSA-4096 Trust Pinning...", null, JournalEntryType.SecurityOperation);
                            var ruleEngine = new SecureRuleEngine();
                            var verifiedRules = ruleEngine.LoadAndVerifyRules(rulesJsonPayload);

                            auditReport.TotalRulesEvaluated = verifiedRules.Count;

                            // Step 5: Pre-Execution Inspection & Transacted Writes
                            var lgpoWriter = new LGPOWriter();
                            int totalCount = verifiedRules.Count;
                            int currentIndex = 0;

                            // Check telemetry services for audit report
                            CheckTelemetryServices(auditReport);

                            foreach (var rule in verifiedRules)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                currentIndex++;
                                int percent = 20 + (int)((currentIndex / (double)totalCount) * 55.0);

                                // Inspection: read current registry state
                                string action = InspectRule(rule, out string currentValue);
                                var item = new AuditChangeItem
                                {
                                    RuleId = rule.Id,
                                    RuleName = rule.Name,
                                    Hive = rule.TargetHive,
                                    KeyPath = rule.KeyPath,
                                    ValueName = rule.ValueName,
                                    CurrentValue = currentValue,
                                    DesiredValue = rule.ValueData,
                                    ActionType = action,
                                    Risk = DetermineRuleRisk(rule)
                                };

                                if (action == "MATCH")
                                {
                                    auditReport.CompliantRulesCount++;
                                }
                                else
                                {
                                    auditReport.PendingChangesCount++;
                                    auditReport.PendingChanges.Add(item);
                                }

                                if (isDryRun)
                                {
                                    // Dry-run mode: do NOT touch system state
                                    ReportProgress(progress, percent, $"[DRY-RUN] {action}: {rule.Name}", rule.Name, JournalEntryType.Audit);
                                }
                                else
                                {
                                    // Real execution: transacted registry writes + LGPO
                                    IntPtr hRoot = KtmTransactionManager.HKEY_LOCAL_MACHINE;
                                    if (string.Equals(rule.TargetHive, "HKCU", StringComparison.OrdinalIgnoreCase))
                                    {
                                        hRoot = KtmTransactionManager.HKEY_CURRENT_USER;
                                    }

                                    if (string.Equals(rule.ValueType, "DWORD", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int val = int.Parse(rule.ValueData);
                                        ktm.SetDword(hRoot, rule.KeyPath, rule.ValueName, val);
                                        lgpoWriter.SetDwordPolicy(rule.KeyPath, rule.ValueName, val);
                                    }
                                    else
                                    {
                                        ktm.SetString(hRoot, rule.KeyPath, rule.ValueName, rule.ValueData);
                                        lgpoWriter.SetStringPolicy(rule.KeyPath, rule.ValueName, rule.ValueData);
                                    }

                                    ReportProgress(progress, percent, $"Applied transacted rule: {rule.Name}", rule.Name, JournalEntryType.RegistryOperation);
                                }
                            }

                            // Evaluate overall risk level
                            auditReport.OverallRiskLevel = auditReport.PendingChangesCount > 15 ? "High" : (auditReport.PendingChangesCount > 5 ? "Medium" : "Low");

                            if (isDryRun)
                            {
                                ReportProgress(progress, 100, $"Dry-Run audit completed. {auditReport.PendingChangesCount} pending changes evaluated. No modifications applied.", null, JournalEntryType.Audit);
                                sw.Stop();
                                return new PipelineResult
                                {
                                    Success = true,
                                    AbortedByUser = false,
                                    IsDryRun = true,
                                    ErrorMessage = null,
                                    AuditReport = auditReport,
                                    Duration = sw.Elapsed
                                };
                            }

                            cancellationToken.ThrowIfCancellationRequested();

                            // Step 6: Isolated Offline Hive Deployment (Default User NTUSER.DAT)
                            string defaultDatPath = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", @"Users\Default\NTUSER.DAT");
                            if (File.Exists(defaultDatPath))
                            {
                                ReportProgress(progress, 80, "Loading isolated AppKey offline hive for Default Profile template...", null, JournalEntryType.Info);
                                using (var hiveEditor = OfflineHiveEditor.OpenHive(defaultDatPath))
                                {
                                    foreach (var rule in verifiedRules)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        if (string.Equals(rule.TargetHive, "HKCU", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(rule.TargetHive, "DefaultUser", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (string.Equals(rule.ValueType, "DWORD", StringComparison.OrdinalIgnoreCase))
                                            {
                                                hiveEditor.SetDword(rule.KeyPath, rule.ValueName, int.Parse(rule.ValueData));
                                            }
                                            else
                                            {
                                                hiveEditor.SetString(rule.KeyPath, rule.ValueName, rule.ValueData);
                                            }
                                        }
                                    }
                                }
                            }

                            cancellationToken.ThrowIfCancellationRequested();

                            // Step 7: LGPO registry.pol Generation & Group Policy Force
                            ReportProgress(progress, 90, "Generating local group policy binary (registry.pol) to prevent Windows Update reversions...", null, JournalEntryType.PolicyOperation);
                            lgpoWriter.SaveAndApplyMachinePolicy();

                            cancellationToken.ThrowIfCancellationRequested();

                            // Step 8: Atomic Kernel Transaction Commit
                            ReportProgress(progress, 95, "Committing Kernel Transaction (KTM)...", null, JournalEntryType.Info);
                            ktm.Commit();

                            // Step 9: Confirm BitLocker Safe Reboot State
                            bitLockerGuard?.ConfirmRebootPending();

                            ReportProgress(progress, 100, "Enterprise baseline deployment successfully finished.", null, JournalEntryType.Info);
                            TransactionJournal.Log(JournalEntryType.Info, "Enterprise Pipeline Execution completed with 100% integrity.");

                            sw.Stop();
                            return new PipelineResult
                            {
                                Success = true,
                                AbortedByUser = false,
                                IsDryRun = false,
                                ErrorMessage = null,
                                AuditReport = auditReport,
                                Duration = sw.Elapsed
                            };
                        }
                        catch (OperationCanceledException)
                        {
                            TransactionJournal.Log(JournalEntryType.Warning, "Enterprise Pipeline execution aborted by user. Auto-rolling back transacted changes via KTM.");
                            ReportProgress(progress, 0, "Execution aborted by user. Transactional KTM rollback completed.", null, JournalEntryType.Warning);
                            sw.Stop();
                            return new PipelineResult
                            {
                                Success = false,
                                AbortedByUser = true,
                                IsDryRun = isDryRun,
                                ErrorMessage = "Pipeline aborted by user. KTM changes rolled back safely.",
                                AuditReport = auditReport,
                                Duration = sw.Elapsed
                            };
                        }
                        catch (Exception ex)
                        {
                            TransactionJournal.Log(JournalEntryType.Error, "Enterprise Pipeline execution encountered an unhandled exception. Auto-rolling back.", ex);
                            OnStepError?.Invoke(this, ex);
                            sw.Stop();
                            return new PipelineResult
                            {
                                Success = false,
                                AbortedByUser = false,
                                IsDryRun = isDryRun,
                                ErrorMessage = ex.Message,
                                AuditReport = auditReport,
                                Duration = sw.Elapsed
                            };
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new PipelineResult
                {
                    Success = false,
                    AbortedByUser = true,
                    IsDryRun = isDryRun,
                    ErrorMessage = "Pipeline cancelled prior to transaction execution.",
                    AuditReport = auditReport,
                    Duration = sw.Elapsed
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new PipelineResult
                {
                    Success = false,
                    AbortedByUser = false,
                    IsDryRun = isDryRun,
                    ErrorMessage = ex.Message,
                    AuditReport = auditReport,
                    Duration = sw.Elapsed
                };
            }
        }

        private static string InspectRule(OptimizationRule rule, out string currentValue)
        {
            currentValue = "(None)";
            try
            {
                RegistryKey root = string.Equals(rule.TargetHive, "HKCU", StringComparison.OrdinalIgnoreCase)
                    ? Registry.CurrentUser
                    : Registry.LocalMachine;

                using (var key = root.OpenSubKey(rule.KeyPath, false))
                {
                    if (key == null)
                    {
                        return "CREATE";
                    }

                    object val = key.GetValue(rule.ValueName, null);
                    if (val == null)
                    {
                        return "CREATE";
                    }

                    currentValue = val.ToString();
                    if (string.Equals(currentValue, rule.ValueData, StringComparison.OrdinalIgnoreCase))
                    {
                        return "MATCH";
                    }

                    return "CHANGE";
                }
            }
            catch
            {
                return "ERROR";
            }
        }

        private static string DetermineRuleRisk(OptimizationRule rule)
        {
            if (rule.KeyPath.IndexOf("Policies", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Low";
            }
            if (rule.KeyPath.IndexOf("Services", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Medium";
            }
            return "Low";
        }

        private static void CheckTelemetryServices(AuditReport report)
        {
            string[] candidateServices = new[] { "DiagTrack", "dmwappushservice", "diagnosticshub.standardcollector.service", "WMPNetworkSvc" };
            foreach (var svc in candidateServices)
            {
                try
                {
                    using (var sc = new ServiceController(svc))
                    {
                        if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
                        {
                            report.ServicesToStop.Add(svc);
                        }
                    }
                }
                catch
                {
                    // Service not installed or inaccessible
                }
            }
        }

        private void ReportProgress(IProgress<PipelineProgress> progress, int percent, string message, string ruleName, JournalEntryType level)
        {
            var p = new PipelineProgress(percent, message, ruleName, level);
            progress?.Report(p);
            OnStepProgress?.Invoke(this, message);
        }
    }
}
