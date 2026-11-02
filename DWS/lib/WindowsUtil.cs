using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DWS_Lite.lib
{
    internal class WindowsUtil
    {
        public static object ReadSubKeyValue(string subKey, string keyName)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var rkSubKey = baseKey.OpenSubKey(subKey))
                {
                    if (rkSubKey == null) return null;
                    return rkSubKey.GetValue(keyName);
                }
            }
            catch
            {
                return null;
            }
        }

        public static int GetWindowsBuildNumber()
        {
            try
            {
                var val = ReadSubKeyValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
                return val != null ? Convert.ToInt32(val) : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static bool UAC_Status()
        {
            try
            {
                var val = ReadSubKeyValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
                return val != null && !Convert.ToBoolean(val);
            }
            catch
            {
                return false;
            }
        }

        public static int SystemRestore_Status()
        {
            try
            {
                var val = ReadSubKeyValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "RPSessionInterval");
                if (val != null) return Convert.ToInt32(val);
                // On Windows 10 & 11, RPSessionInterval is legacy and usually absent,
                // but System Restore functionality is available via WMI / PowerShell.
                return 1;
            }
            catch
            {
                return 1;
            }
        }

        public static string GetProductName()
        {
            var val = ReadSubKeyValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
            return val != null ? Convert.ToString(val) : string.Empty;
        }

        public static string GetSystemBuild()
        {
            var val = ReadSubKeyValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "BuildLabEx");
            return val != null ? Convert.ToString(val) : string.Empty;
        }

        /// <summary>
        /// Checks if BitLocker protection is active on the system drive.
        /// If BitLocker is ON, modifying BCD bootmenupolicy (e.g. legacy F8) can prompt for recovery keys.
        /// </summary>
        public static bool IsBitLockerActive()
        {
            try
            {
                using (var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoLogo -NoProfile -NonInteractive -Command \"try { (Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop).ProtectionStatus.ToString() } catch { 'Off' }\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                })
                {
                    proc.Start();
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);
                    return output.IndexOf("On", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
