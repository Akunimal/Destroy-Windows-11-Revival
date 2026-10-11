using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace DWS_Lite.lib
{
    /// <summary>One registry value that DWS writes when it de-spies the system.</summary>
    internal class TweakedRegistryValue
    {
        public readonly string Hive;   // "HKCU" or "HKLM"
        public readonly string Path;  // subkey path, no hive prefix
        public readonly string Value; // value name

        public TweakedRegistryValue(string hive, string path, string value)
        {
            Hive = hive;
            Path = path;
            Value = value;
        }
    }

    /// <summary>
    /// Reversible registry tweaks.
    ///
    /// DWS writes 55 registry values. This module snapshots the "before" state of
    /// every one of them into a timestamped backup folder so the user can undo
    /// everything, plus a plain .reg export of the parent keys for manual
    /// inspection.
    /// </summary>
    internal static class RegistryBackup
    {
        // Every registry value the program modifies, kept in sync with MainDwsForm.
        internal static readonly TweakedRegistryValue[] TweakedValues = new[]
        {
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "CortanaEnabled"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitInkCollection"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableWindowsLocationProvider"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocationScripting"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableSensors"),
            new TweakedRegistryValue("HKLM", @"SYSTEM\CurrentControlSet\Services\lfsvc\Service\Configuration", "Status"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}", "SensorPermissionState"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Siuf\Rules", "PeriodInNanoSeconds"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\TabletPC", "PreventHandwritingDataSharing"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports", "PreventHandwritingErrorReports"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableInventory"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreenCamera"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Input\TIPC", "Enabled"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Biometrics", "Enabled"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\CredUI", "DisablePasswordReveal"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync", "SyncPolicy"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Personalization", "Enabled"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\BrowserSettings", "Enabled"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Credentials", "Enabled"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Language", "Enabled"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Accessibility", "Enabled"),
            new TweakedRegistryValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Windows", "Enabled"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SpyNetReporting"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SubmitSamplesConsent"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "MAPSReporting"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\MRT", "DontReportInfectionInformation"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled"),
            new TweakedRegistryValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton"),
            new TweakedRegistryValue("HKCU", @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot"),
            new TweakedRegistryValue("HKLM", @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot"),
            new TweakedRegistryValue("HKCU", @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis"),
            new TweakedRegistryValue("HKLM", @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis"),
            new TweakedRegistryValue("HKLM", @"Software\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement"),
            new TweakedRegistryValue("HKLM", @"Software\Policies\Microsoft\Windows\WindowsAI", "TurnOffSavingSnapshots"),
            new TweakedRegistryValue("HKCU", @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions"),
            new TweakedRegistryValue("HKLM", @"Software\Policies\Microsoft\Windows\Windows Search", "AllowCortana"),
            new TweakedRegistryValue("HKLM", @"Software\Policies\Microsoft\Windows\Windows Search", "CortanaConsent"),
            new TweakedRegistryValue("HKLM", @"Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests"),
            new TweakedRegistryValue("HKLM", @"Software\Policies\Microsoft\Dsh", "DisableWidgetsBoard"),
            new TweakedRegistryValue("HKLM", @"Software\Policies\Microsoft\Dsh", "DisableWidgetsOnLockScreen"),
            new TweakedRegistryValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388SpotlightEnabled"),
            new TweakedRegistryValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled"),
            new TweakedRegistryValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled"),
            new TweakedRegistryValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled"),
            new TweakedRegistryValue("HKLM", @"SYSTEM\CurrentControlSet\Services\WSAIFabricSvc", "Start"),
            new TweakedRegistryValue("HKLM", @"SYSTEM\CurrentControlSet\Control\BitLocker", "PreventDeviceEncryption"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA"),
            new TweakedRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC"),
        };

        private const string ManifestName = "manifest.txt";

        /// <summary>Where backups live: beside the exe, or in Documents if that fails.</summary>
        internal static string BackupRoot
        {
            get
            {
                try
                {
                    var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var candidate = Path.Combine(dir, "DWS-backups");
                        Directory.CreateDirectory(candidate);
                        return candidate;
                    }
                }
                // ReSharper disable once EmptyGeneralCatchClause
                catch (Exception)
                {
                }
                var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "DWS-backups");
                Directory.CreateDirectory(docs);
                return docs;
            }
        }

        /// <summary>Creates a new timestamped backup and returns its path.</summary>
        internal static string CreateBackup(Action<string> log)
        {
            var folder = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss",
                CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);

            var sb = new StringBuilder();
            var snapshotted = 0;
            foreach (var entry in TweakedValues)
            {
                try
                {
                    using (var key = OpenRoot(entry.Hive).OpenSubKey(entry.Path, false))
                    {
                        var existed = key != null && key.GetValueNames().Length > 0 &&
                                      containsValue(key, entry.Value);
                        if (existed)
                        {
                            var kind = key.GetValueKind(entry.Value);
                            var raw = key.GetValue(entry.Value);
                            sb.Append(Encode(entry.Hive)).Append('\t')
                                .Append(Encode(entry.Path)).Append('\t')
                                .Append(Encode(entry.Value)).Append('\t')
                                .Append("1\t")
                                .Append(Encode(kind.ToString())).Append('\t')
                                .Append(Encode(Serialize(raw, kind)))
                                .Append('\n');
                            snapshotted++;
                        }
                        else
                        {
                            sb.Append(Encode(entry.Hive)).Append('\t')
                                .Append(Encode(entry.Path)).Append('\t')
                                .Append(Encode(entry.Value)).Append('\t')
                                .Append("0\t\t\n");
                        }
                    }
                }
                catch (Exception ex)
                {
                    log(string.Format("Warning: could not snapshot {0}\\{1}\\{2}: {3}", entry.Hive, entry.Path,
                        entry.Value, ex.Message));
                }
            }

            File.WriteAllText(Path.Combine(folder, ManifestName), sb.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(folder, "README.txt"),
                "DWS 11 registry backup. Restore with the \"Restore registry backup\" button " +
                "in the Utilities tab.\r\nA .reg export of the affected keys is in this folder " +
                "for manual import from regedit.\r\n", Encoding.UTF8);

            ExportParentKeys(folder, log);
            log(string.Format("Registry backup created: {0} ({1} of {2} values present before).",
                folder, snapshotted, TweakedValues.Length));
            return folder;
        }

        private static bool containsValue(RegistryKey key, string name)
        {
            foreach (var n in key.GetValueNames())
            {
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Parent keys exported as .reg files so the user can inspect them.</summary>
        private static readonly string[] ExportKeys = new[]
        {
            @"HKCU\Software\Policies\Microsoft\Windows",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Search",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync",
            @"HKCU\SOFTWARE\Microsoft\InputPersonalization",
            @"HKCU\SOFTWARE\Microsoft\Siuf\Rules",
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows",
            @"HKLM\Software\Policies\Microsoft\Dsh",
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Spynet",
            @"HKLM\SOFTWARE\Policies\Microsoft\Biometrics",
            @"HKLM\SOFTWARE\Policies\Microsoft\MRT",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            @"HKLM\SYSTEM\CurrentControlSet\Control\BitLocker",
            @"HKLM\SYSTEM\CurrentControlSet\Services\WSAIFabricSvc"
        };

        private static void ExportParentKeys(string folder, Action<string> log)
        {
            foreach (var key in ExportKeys)
            {
                var fileName = SanitizeFileName(key) + ".reg";
                RunReg(string.Format("export \"{0}\" \"{1}\" /y", key,
                    Path.Combine(folder, fileName)));
            }
        }

        private static string SanitizeFileName(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return sb.ToString();
        }

        /// <summary>Backups available on this machine, newest first.</summary>
        internal static List<string> ListBackups()
        {
            var result = new List<string>();
            try
            {
                var root = BackupRoot;
                if (!Directory.Exists(root)) return result;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (File.Exists(Path.Combine(dir, ManifestName))) result.Add(dir);
                }
                result.Sort();
                result.Reverse();
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception)
            {
            }
            return result;
        }

        /// <summary>
        /// Reverses a backup: restores previous values and deletes the values that
        /// did not exist before DWS ran.
        /// </summary>
        internal static void Restore(string backupFolder, Action<string> log)
        {
            var manifest = Path.Combine(backupFolder, ManifestName);
            if (!File.Exists(manifest)) throw new FileNotFoundException("Backup manifest not found.", manifest);

            var lines = File.ReadAllLines(manifest, Encoding.UTF8);
            var restored = 0;
            var deleted = 0;
            var failed = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length < 6) continue;
                var hive = Decode(parts[0]);
                var path = Decode(parts[1]);
                var name = Decode(parts[2]);
                var existed = parts[3] == "1";
                var kind = Decode(parts[4]);
                var data = Decode(parts[5]);

                try
                {
                    if (!existed)
                    {
                        // Did not exist before DWS: remove it again.
                        using (var key = OpenRoot(hive).OpenSubKey(path, true))
                        {
                            if (key == null) continue;
                            if (containsValue(key, name))
                            {
                                key.DeleteValue(name, false);
                                deleted++;
                            }
                        }
                        continue;
                    }

                    using (var key = OpenRoot(hive).CreateSubKey(path))
                    {
                        if (key == null) continue;
                        var valueKind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), kind, true);
                        key.SetValue(name, Deserialize(data, valueKind), valueKind);
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    // One inaccessible key must never abort the whole restore.
                    failed++;
                    log(string.Format("Warning: could not restore {0}\\{1}\\{2}: {3}", hive, path, name, ex.Message));
                }
            }

            log(string.Format(
                "Registry restore finished from {0}: {1} value(s) restored, {2} value(s) removed, {3} skipped (access denied).",
                Path.GetFileName(backupFolder), restored, deleted, failed));
        }

        private static string Serialize(object raw, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord:
                case RegistryValueKind.QWord:
                    return Convert.ToString(raw, CultureInfo.InvariantCulture);
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    return raw == null ? string.Empty : Convert.ToString(raw, CultureInfo.InvariantCulture);
                case RegistryValueKind.MultiString:
                    var arr = raw as string[];
                    return string.Join("\n", arr ?? new string[0]);
                default:
                    var bytes = raw as byte[];
                    return bytes == null ? string.Empty : Convert.ToBase64String(bytes);
            }
        }

        private static object Deserialize(string data, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    return int.Parse(data, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord:
                    return long.Parse(data, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    return data;
                case RegistryValueKind.MultiString:
                    return data.Split('\n');
                default:
                    return Convert.FromBase64String(string.IsNullOrEmpty(data) ? string.Empty : data);
            }
        }

        private static RegistryKey OpenRoot(string hive)
        {
            return hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase)
                ? Registry.LocalMachine
                : Registry.CurrentUser;
        }

        private static void RunReg(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("reg.exe", arguments) { CreateNoWindow = true, UseShellExecute = false };
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(30000);
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception)
            {
            }
        }

        private static string Encode(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
            return Convert.ToBase64String(bytes);
        }

        private static string Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var bytes = Convert.FromBase64String(s);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
