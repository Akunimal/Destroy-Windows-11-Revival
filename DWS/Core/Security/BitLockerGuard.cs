using System;
using System.Diagnostics;
using System.Management;
using DWS.Core.Logging;

namespace DWS.Core.Security
{
    /// <summary>
    /// BitLocker Guard with TPM PCR Measurement Protection.
    /// Checks BitLocker encryption status and coordinates temporary protection suspension
    /// (RebootCount = 1) before applying system boot or low-level kernel configuration changes.
    /// Prevents users from being locked out by BitLocker 48-digit recovery key prompts.
    /// </summary>
    public sealed class BitLockerGuard : IDisposable
    {
        private readonly string _driveLetter;
        private bool _wasSuspended;
        private bool _rebootConfirmed;
        private bool _isDisposed;

        public bool WasSuspended => _wasSuspended;
        public string DriveLetter => _driveLetter;

        public event CoreMessageEventHandler OnStatusChanged;
        public event CoreErrorEventHandler OnGuardError;

        private BitLockerGuard(string driveLetter, bool wasSuspended)
        {
            _driveLetter = driveLetter;
            _wasSuspended = wasSuspended;
        }

        public static bool IsProtectionActive(string driveLetter = "C:")
        {
            try
            {
                // Method 1: WMI CIMV2 Security
                using (var searcher = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftVolumeEncryption",
                    $"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{driveLetter}'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var status = obj["ProtectionStatus"];
                        if (status != null && Convert.ToInt32(status) == 1)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to PowerShell
                try
                {
                    using (var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-NoLogo -NoProfile -NonInteractive -Command \"try {{ (Get-BitLockerVolume -MountPoint '{driveLetter}' -ErrorAction Stop).ProtectionStatus.ToString() }} catch {{ 'Off' }}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true
                        }
                    })
                    {
                        proc.Start();
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(5000);
                        return output.IndexOf("On", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
                catch { }
            }

            return false;
        }

        public static BitLockerGuard ProtectSession(string driveLetter = "C:", int rebootCount = 1)
        {
            bool isActive = IsProtectionActive(driveLetter);
            if (!isActive)
            {
                TransactionJournal.Log(JournalEntryType.Info, $"BitLocker is not active on {driveLetter}. No suspension required.");
                return new BitLockerGuard(driveLetter, false);
            }

            TransactionJournal.Log(JournalEntryType.SecurityOperation, $"BitLocker ACTIVE on {driveLetter}. Suspending protection (RebootCount={rebootCount}) to safeguard TPM PCRs...");

            bool suspended = false;
            try
            {
                // Attempt suspension via PowerShell
                using (var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoLogo -NoProfile -NonInteractive -Command \"Suspend-BitLocker -MountPoint '{driveLetter}' -RebootCount {rebootCount} -ErrorAction Stop\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                })
                {
                    proc.Start();
                    proc.WaitForExit(10000);
                    suspended = (proc.ExitCode == 0);
                }

                if (suspended)
                {
                    TransactionJournal.Log(JournalEntryType.SecurityOperation, $"BitLocker protection successfully suspended on {driveLetter} for {rebootCount} reboot(s).");
                }
                else
                {
                    TransactionJournal.Log(JournalEntryType.Warning, $"Suspend-BitLocker returned non-zero code on {driveLetter}.");
                }
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, $"Failed to suspend BitLocker on {driveLetter}", ex);
            }

            return new BitLockerGuard(driveLetter, suspended);
        }

        public void ConfirmRebootPending()
        {
            _rebootConfirmed = true;
            TransactionJournal.Log(JournalEntryType.Info, $"BitLocker guard confirmed reboot pending. Suspension retained for reboot PCR recalculation.");
            OnStatusChanged?.Invoke(this, "BitLocker PCR Guard: Reboot pending confirmed.");
        }

        public void ResumeProtectionNow()
        {
            if (!_wasSuspended) return;

            try
            {
                TransactionJournal.Log(JournalEntryType.SecurityOperation, $"Resuming BitLocker protection immediately on {_driveLetter}...");
                using (var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoLogo -NoProfile -NonInteractive -Command \"Resume-BitLocker -MountPoint '{_driveLetter}' -ErrorAction SilentlyContinue\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                })
                {
                    proc.Start();
                    proc.WaitForExit(10000);
                }
                _wasSuspended = false;
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, $"Error resuming BitLocker on {_driveLetter}", ex);
                OnGuardError?.Invoke(this, ex);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            // If an unhandled abort happened and reboot wasn't confirmed, safely resume protection
            if (_wasSuspended && !_rebootConfirmed)
            {
                ResumeProtectionNow();
            }

            _isDisposed = true;
        }
    }
}
