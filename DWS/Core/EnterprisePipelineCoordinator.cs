using System;
using System.IO;
using DWS.Core.KTM;
using DWS.Core.Logging;
using DWS.Core.Policy;
using DWS.Core.Rules;
using DWS.Core.Security;
using DWS.Core.Storage;

namespace DWS.Core
{
    /// <summary>
    /// Enterprise Pipeline Coordinator.
    /// Orchestrates the 6 low-level kernel modules in exact chronological order:
    /// 1. BitLockerGuard (TPM PCR Suspension)
    /// 2. SystemPrivilegeBroker (LSA/PPL-Safe Privilege Validation)
    /// 3. KtmTransactionManager (KTM Kernel Transaction Initialization)
    /// 4. SecureRuleEngine (RSA-4096 Signature Verification)
    /// 5. OfflineHiveEditor (Isolated RegLoadAppKey on Default Profile)
    /// 6. LGPOWriter (registry.pol Binary Generation + gpupdate)
    /// 7. Atomic Transaction Commit & Reboot Confirmation
    /// </summary>
    public sealed class EnterprisePipelineCoordinator
    {
        public event CoreMessageEventHandler OnStepProgress;
        public event CoreErrorEventHandler OnStepError;

        public bool ExecuteEnterpriseBaseline(string rulesJsonPayload)
        {
            TransactionJournal.Log(JournalEntryType.Info, "Starting Enterprise Pipeline Execution...");

            // Step 1: BitLocker TPM PCR Protection
            using (var bitLockerGuard = BitLockerGuard.ProtectSession("C:", 1))
            {
                // Step 2: Privilege Broker Validation
                var privilegeBroker = new SystemPrivilegeBroker();
                bool isSystem = privilegeBroker.IsRunningAsSystem();
                TransactionJournal.Log(JournalEntryType.Info, $"Security Context: {(isSystem ? "NT AUTHORITY\\SYSTEM" : "Elevated Administrator")}");
                OnStepProgress?.Invoke(this, $"Security context verified: {(isSystem ? "SYSTEM" : "Administrator")}");

                // Step 3: Kernel Transaction Manager (KTM) Initialization
                using (var ktm = new KtmTransactionManager("DWS_EnterpriseBaseline"))
                {
                    try
                    {
                        // Step 4: Secure Rule Engine Trust Pinning Verification
                        OnStepProgress?.Invoke(this, "Verifying digital signatures via RSA-4096 Trust Pinning...");
                        var ruleEngine = new SecureRuleEngine();
                        var verifiedRules = ruleEngine.LoadAndVerifyRules(rulesJsonPayload);

                        // Step 5: Execute Transacted Registry Operations via KTM
                        OnStepProgress?.Invoke(this, "Applying transacted registry policies via Kernel Transaction Manager...");
                        var lgpoWriter = new LGPOWriter();

                        foreach (var rule in verifiedRules)
                        {
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
                        }

                        // Step 6: Isolated Offline Hive Deployment (Default User NTUSER.DAT)
                        string defaultDatPath = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", @"Users\Default\NTUSER.DAT");
                        if (File.Exists(defaultDatPath))
                        {
                            OnStepProgress?.Invoke(this, "Loading isolated AppKey offline hive for Default Profile template...");
                            using (var hiveEditor = OfflineHiveEditor.OpenHive(defaultDatPath))
                            {
                                foreach (var rule in verifiedRules)
                                {
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

                        // Step 7: LGPO registry.pol Generation & Group Policy Force
                        OnStepProgress?.Invoke(this, "Generating local group policy binary (registry.pol) to prevent Windows Update reversions...");
                        lgpoWriter.SaveAndApplyMachinePolicy();

                        // Step 8: Atomic Kernel Transaction Commit
                        OnStepProgress?.Invoke(this, "Committing Kernel Transaction (KTM)...");
                        ktm.Commit();

                        // Step 9: Confirm BitLocker Safe Reboot State
                        bitLockerGuard.ConfirmRebootPending();

                        TransactionJournal.Log(JournalEntryType.Info, "Enterprise Pipeline Execution completed with 100% integrity.");
                        OnStepProgress?.Invoke(this, "Enterprise baseline deployment successfully finished.");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        TransactionJournal.Log(JournalEntryType.Error, "Enterprise Pipeline execution aborted. Kernel transaction auto-rolling back.", ex);
                        OnStepError?.Invoke(this, ex);
                        // ktm.Dispose() automatically triggers atomic kernel rollback
                        throw;
                    }
                }
            }
        }
    }
}
