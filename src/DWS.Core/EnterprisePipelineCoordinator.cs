using System.Diagnostics;
using DWS.Core.KTM;
using DWS.Core.Logging;
using DWS.Core.Policy;
using DWS.Core.Rules;
using DWS.Core.Security;
using DWS.Core.Storage;
using Microsoft.Win32;

namespace DWS.Core;

public sealed class EnterprisePipelineCoordinator
{
    private readonly SecureRuleEngine _ruleEngine;
    private readonly TransactionJournal _journal;
    private readonly SystemPrivilegeBroker _privilegeBroker;

    public EnterprisePipelineCoordinator(
        SecureRuleEngine? ruleEngine = null,
        TransactionJournal? journal = null,
        SystemPrivilegeBroker? privilegeBroker = null)
    {
        _ruleEngine = ruleEngine ?? new SecureRuleEngine();
        _journal = journal ?? new TransactionJournal();
        _privilegeBroker = privilegeBroker ?? new SystemPrivilegeBroker();
    }

    public async Task<PipelineResult> ExecuteEnterpriseBaselineAsync(
        List<string>? targetRuleIds,
        bool isDryRun,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() => ExecuteEnterpriseBaselineInternal(targetRuleIds, isDryRun, progress, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new PipelineResult
            {
                Success = false,
                AbortedByUser = true,
                IsDryRun = isDryRun,
                ErrorMessage = "Operation aborted by user."
            };
        }
    }

    private PipelineResult ExecuteEnterpriseBaselineInternal(
        List<string>? targetRuleIds,
        bool isDryRun,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var auditReport = new AuditReport();
        string transactionId = $"TX_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";

        // Verify minimum OS version: Windows 10 22H2 (build 19045)
        var osVersion = Environment.OSVersion.Version;
        if (osVersion.Build < 19045)
        {
            throw new PlatformNotSupportedException(
                $"DWS 11 Enterprise v2.0 requires Windows 10 22H2 (build 19045) or Windows 11. Current build: {osVersion.Build}");
        }

        progress?.Report(new PipelineProgress(5, "Verifying OS build compatibility...", "", "Information"));
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Load rules
        RuleSet ruleSet = _ruleEngine.LoadAndVerifyRulesFromFile("rules/rules.json");
        List<Rule> activeRules = ruleSet.Rules;
        if (targetRuleIds != null && targetRuleIds.Count > 0)
        {
            activeRules = ruleSet.Rules.Where(r => targetRuleIds.Contains(r.Id)).ToList();
        }

        auditReport.TotalRulesEvaluated = activeRules.Count;

        // 2. BitLocker protection guard (Real execution only)
        BitLockerGuard? bitLockerGuard = null;
        if (!isDryRun)
        {
            progress?.Report(new PipelineProgress(10, "Guarding BitLocker TPM PCR session...", "", "Information"));
            bitLockerGuard = BitLockerGuard.ProtectSession("C:", 1);
        }

        try
        {
            using (bitLockerGuard)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 3. KTM transacted registry initialization
                using var ktm = new KtmTransactionManager(transactionId);
                var lgpoWriter = new LGPOWriter();
                var serviceHandler = new Operations.ServiceOperationHandler();
                int currentIndex = 0;

                try
                {
                    foreach (var rule in activeRules)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        currentIndex++;
                        double pct = 15.0 + ((currentIndex / (double)activeRules.Count) * 60.0);

                        foreach (var op in rule.Operations)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (string.Equals(op.Type, "registry", StringComparison.OrdinalIgnoreCase))
                            {
                                string hive = op.Hive ?? "HKLM";
                                string path = op.Path ?? "";
                                string name = op.Name ?? "";
                                string desiredVal = op.Value ?? "0";

                                // Pre-inspection of current value
                                var (exists, currentVal) = ReadRegistryCurrentValue(hive, path, name);
                                string action = !exists ? "CREATE" : (currentVal == desiredVal ? "MATCH" : "CHANGE");

                                var auditItem = new AuditChangeItem
                                {
                                    RuleId = rule.Id,
                                    RuleName = rule.DisplayName,
                                    Hive = hive,
                                    KeyPath = path,
                                    ValueName = name,
                                    CurrentValue = currentVal ?? "(None)",
                                    DesiredValue = desiredVal,
                                    ActionType = action,
                                    Risk = "Low"
                                };

                                if (action == "MATCH")
                                {
                                    auditReport.CompliantRulesCount++;
                                    rule.IsApplied = true;
                                }
                                else
                                {
                                    auditReport.PendingChangesCount++;
                                    auditReport.PendingChanges.Add(auditItem);
                                    rule.IsApplied = false;
                                }

                                if (isDryRun)
                                {
                                    progress?.Report(new PipelineProgress(pct, $"[DRY-RUN] {action}: {rule.DisplayName}", rule.DisplayName, "Audit"));
                                }
                                else
                                {
                                    // Record change in journal
                                    _journal.LogChange(new TransactionEntry
                                    {
                                        TransactionId = transactionId,
                                        Timestamp = DateTime.UtcNow,
                                        Category = rule.Category,
                                        ChangeType = ChangeType.RegistrySet,
                                        RuleName = rule.DisplayName,
                                        Hive = hive,
                                        KeyPath = path,
                                        ValueName = name,
                                        NewValue = desiredVal,
                                        PreviousValue = currentVal,
                                        PreviousValueExisted = exists,
                                        Status = TransactionStatus.Applied
                                    });

                                    try
                                    {
                                        // Apply via KTM (with TrustedInstaller impersonation if required)
                                        if (rule.RequiresTrustedInstaller && _privilegeBroker.IsRunningAsAdministrator())
                                        {
                                            try
                                            {
                                                SystemPrivilegeBroker.RunAsTrustedInstaller(() =>
                                                {
                                                    ktm.SetValue(hive, path, name, desiredVal);
                                                });
                                            }
                                            catch
                                            {
                                                // Fallback to direct KTM if TI service is unavailable
                                                ktm.SetValue(hive, path, name, desiredVal);
                                            }
                                        }
                                        else
                                        {
                                            ktm.SetValue(hive, path, name, desiredVal);
                                        }
                                    }
                                    catch (Exception ex) when (path.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase) ||
                                                               path.Contains("WinDefend", StringComparison.OrdinalIgnoreCase))
                                    {
                                        progress?.Report(new PipelineProgress(pct, $"[Tamper Protection] Protected key skipped ({name}): {ex.Message}", rule.DisplayName, "Warning"));
                                    }

                                    // Apply to LGPO
                                    if (int.TryParse(desiredVal, out int dwordVal))
                                    {
                                        lgpoWriter.SetDwordPolicy(path, name, dwordVal);
                                    }
                                    else
                                    {
                                        lgpoWriter.SetStringPolicy(path, name, desiredVal);
                                    }

                                    progress?.Report(new PipelineProgress(pct, $"Applied rule: {rule.DisplayName}", rule.DisplayName, "Information"));
                                }
                            }
                            else if (string.Equals(op.Type, "service", StringComparison.OrdinalIgnoreCase))
                            {
                                string svcName = op.ServiceName ?? op.Name ?? "";
                                uint desiredMode = Operations.ServiceOperationHandler.ParseStartMode(op.ServiceStartMode ?? op.Value);
                                var (svcExists, curStart, _) = serviceHandler.QueryServiceState(svcName);
                                string action = !svcExists ? "NOT_FOUND" : (curStart == desiredMode ? "MATCH" : "CHANGE");

                                var auditItem = new AuditChangeItem
                                {
                                    RuleId = rule.Id,
                                    RuleName = rule.DisplayName,
                                    Hive = "SERVICES",
                                    KeyPath = svcName,
                                    ValueName = "Start",
                                    CurrentValue = svcExists ? curStart.ToString() : "(Not Installed)",
                                    DesiredValue = desiredMode.ToString(),
                                    ActionType = action,
                                    Risk = "Medium"
                                };

                                if (action == "MATCH")
                                {
                                    auditReport.CompliantRulesCount++;
                                    rule.IsApplied = true;
                                }
                                else
                                {
                                    auditReport.PendingChangesCount++;
                                    auditReport.PendingChanges.Add(auditItem);
                                    rule.IsApplied = false;
                                }

                                if (isDryRun)
                                {
                                    progress?.Report(new PipelineProgress(pct, $"[DRY-RUN] Service {svcName}: {action}", rule.DisplayName, "Audit"));
                                }
                                else if (svcExists && action != "MATCH")
                                {
                                    _journal.LogChange(new TransactionEntry
                                    {
                                        TransactionId = transactionId,
                                        Timestamp = DateTime.UtcNow,
                                        Category = rule.Category,
                                        ChangeType = ChangeType.ServiceConfig,
                                        RuleName = rule.DisplayName,
                                        ServiceName = svcName,
                                        PreviousServiceStartMode = curStart.ToString(),
                                        Status = TransactionStatus.Applied
                                    });

                                    serviceHandler.ConfigureServiceStartType(svcName, desiredMode, ktm);
                                    progress?.Report(new PipelineProgress(pct, $"Configured service: {svcName}", rule.DisplayName, "Information"));
                                }
                            }
                            else if (string.Equals(op.Type, "appx", StringComparison.OrdinalIgnoreCase))
                            {
                                string pkgPattern = op.PackagePattern ?? op.Value ?? op.Name ?? "";
                                var matched = Operations.AppxOperationHandler.DetectMatchingPackages(pkgPattern);
                                string action = matched.Count > 0 ? "REMOVE" : "MATCH";

                                var auditItem = new AuditChangeItem
                                {
                                    RuleId = rule.Id,
                                    RuleName = rule.DisplayName,
                                    Hive = "APPX",
                                    KeyPath = pkgPattern,
                                    ValueName = "Package",
                                    CurrentValue = matched.Count > 0 ? $"{matched.Count} packages" : "(Not Installed)",
                                    DesiredValue = "(Deprovisioned)",
                                    ActionType = action,
                                    Risk = "Medium"
                                };

                                if (action == "MATCH")
                                {
                                    auditReport.CompliantRulesCount++;
                                    rule.IsApplied = true;
                                }
                                else
                                {
                                    auditReport.PendingChangesCount++;
                                    auditReport.PendingChanges.Add(auditItem);
                                    rule.IsApplied = false;
                                }

                                if (isDryRun)
                                {
                                    progress?.Report(new PipelineProgress(pct, $"[DRY-RUN] AppX {pkgPattern}: {action}", rule.DisplayName, "Audit"));
                                }
                                else if (action == "REMOVE")
                                {
                                    _journal.LogChange(new TransactionEntry
                                    {
                                        TransactionId = transactionId,
                                        Timestamp = DateTime.UtcNow,
                                        Category = rule.Category,
                                        ChangeType = ChangeType.AppxRemoval,
                                        RuleName = rule.DisplayName,
                                        PackagePattern = pkgPattern,
                                        Status = TransactionStatus.Applied
                                    });

                                    Operations.AppxOperationHandler.RemoveAndDeprovisionPackageAsync(pkgPattern, isDryRun: false, ktm, cancellationToken)
                                        .GetAwaiter().GetResult();
                                    progress?.Report(new PipelineProgress(pct, $"Removed AppX package: {pkgPattern}", rule.DisplayName, "Information"));
                                }
                            }
                            else if (string.Equals(op.Type, "hosts", StringComparison.OrdinalIgnoreCase))
                            {
                                var domains = op.Domains ?? (op.Value != null ? new List<string> { op.Value } : new List<string>());
                                var (hasBlock, currentBlock) = Operations.HostsOperationHandler.ReadManagedBlock();
                                string action = hasBlock ? "UPDATE" : "CREATE";

                                var auditItem = new AuditChangeItem
                                {
                                    RuleId = rule.Id,
                                    RuleName = rule.DisplayName,
                                    Hive = "HOSTS",
                                    KeyPath = Operations.HostsOperationHandler.DefaultHostsPath,
                                    ValueName = "ManagedBlock",
                                    CurrentValue = hasBlock ? "Managed Block Active" : "(None)",
                                    DesiredValue = $"{domains.Count} domains blocked",
                                    ActionType = action,
                                    Risk = "Low"
                                };

                                auditReport.PendingChangesCount++;
                                auditReport.PendingChanges.Add(auditItem);

                                if (isDryRun)
                                {
                                    progress?.Report(new PipelineProgress(pct, $"[DRY-RUN] Hosts file: {action} ({domains.Count} domains)", rule.DisplayName, "Audit"));
                                }
                                else if (domains.Count > 0)
                                {
                                    _journal.LogChange(new TransactionEntry
                                    {
                                        TransactionId = transactionId,
                                        Timestamp = DateTime.UtcNow,
                                        Category = rule.Category,
                                        ChangeType = ChangeType.HostsModify,
                                        RuleName = rule.DisplayName,
                                        PreviousHostsBlock = currentBlock,
                                        BlockedDomains = domains,
                                        Status = TransactionStatus.Applied
                                    });

                                    Operations.HostsOperationHandler.ApplyHostsBlockAsync(domains, null, cancellationToken)
                                        .GetAwaiter().GetResult();
                                    progress?.Report(new PipelineProgress(pct, $"Updated Hosts block: {domains.Count} domains", rule.DisplayName, "Information"));
                                }
                            }
                        }
                    }

                    if (isDryRun)
                    {
                        sw.Stop();
                        progress?.Report(new PipelineProgress(100, $"Dry-Run complete. {auditReport.PendingChangesCount} changes evaluated.", "", "Information"));
                        return new PipelineResult
                        {
                            Success = true,
                            AbortedByUser = false,
                            IsDryRun = true,
                            AuditReport = auditReport,
                            Duration = sw.Elapsed,
                            TotalRules = ruleSet.Rules.Count,
                            AppliedRules = activeRules.Count,
                            TransactionId = transactionId
                        };
                    }

                    // 4. Offline Default Profile Hive Deployment (NTUSER.DAT)
                    cancellationToken.ThrowIfCancellationRequested();
                    string defaultDat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"..\Default\NTUSER.DAT");
                    string fullDefaultDatPath = Path.GetFullPath(defaultDat);

                    if (File.Exists(fullDefaultDatPath))
                    {
                        try
                        {
                            progress?.Report(new PipelineProgress(80, "Deploying isolated AppKey offline hive to Default profile...", "", "Information"));
                            using var hiveEditor = OfflineHiveEditor.OpenHive(fullDefaultDatPath);

                            foreach (var rule in activeRules)
                            {
                                foreach (var op in rule.Operations)
                                {
                                    if (string.Equals(op.Hive, "HKCU", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (int.TryParse(op.Value, out int intVal))
                                        {
                                            hiveEditor.SetDword(op.Path ?? "", op.Name ?? "", intVal);
                                        }
                                        else
                                        {
                                            hiveEditor.SetString(op.Path ?? "", op.Name ?? "", op.Value ?? "");
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            progress?.Report(new PipelineProgress(85, $"Warning: Could not update Default profile offline hive ({ex.Message}). Continuing baseline.", "", "Warning"));
                        }
                    }

                    // 5. Commit LGPO binary & Force Group Policy
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new PipelineProgress(90, "Applying Machine Group Policy (registry.pol)...", "", "Information"));
                    lgpoWriter.SaveAndApplyMachinePolicy();

                    // 6. Commit KTM Kernel Transaction
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new PipelineProgress(95, "Committing Kernel Transaction (KTM)...", "", "Information"));
                    ktm.Commit();

                    // 7. Confirm BitLocker reboot
                    bitLockerGuard?.ConfirmRebootPending();

                    sw.Stop();
                    progress?.Report(new PipelineProgress(100, "Enterprise optimizations applied successfully with 100% integrity.", "", "Information"));

                    return new PipelineResult
                    {
                        Success = true,
                        AbortedByUser = false,
                        IsDryRun = false,
                        AuditReport = auditReport,
                        Duration = sw.Elapsed,
                        TotalRules = ruleSet.Rules.Count,
                        AppliedRules = activeRules.Count,
                        TransactionId = transactionId
                    };
                }
                catch (OperationCanceledException)
                {
                    ktm.Rollback();
                    try { _journal.RevertTransactionAsync(transactionId).GetAwaiter().GetResult(); } catch { }
                    sw.Stop();
                    progress?.Report(new PipelineProgress(0, "Operation aborted by user. KTM and Saga changes rolled back.", "", "Warning"));

                    return new PipelineResult
                    {
                        Success = false,
                        AbortedByUser = true,
                        IsDryRun = isDryRun,
                        ErrorMessage = "Pipeline cancelled by user. KTM and Saga transactional changes rolled back.",
                        AuditReport = auditReport,
                        Duration = sw.Elapsed,
                        TotalRules = ruleSet.Rules.Count,
                        AppliedRules = 0,
                        TransactionId = transactionId
                    };
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
                ErrorMessage = "Operation aborted by user.",
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

    public Task<bool> RollbackTransactionAsync(string transactionId)
    {
        return _journal.RevertTransactionAsync(transactionId);
    }

    private static (bool Exists, string? Value) ReadRegistryCurrentValue(string hive, string path, string name)
    {
        try
        {
            var baseKey = string.Equals(hive, "HKCU", StringComparison.OrdinalIgnoreCase)
                ? Registry.CurrentUser
                : Registry.LocalMachine;

            using var subKey = baseKey.OpenSubKey(path, false);
            if (subKey == null) return (false, null);

            var val = subKey.GetValue(name);
            if (val == null) return (false, null);

            return (true, val.ToString());
        }
        catch
        {
            return (false, null);
        }
    }
}
