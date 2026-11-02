using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using DWS.Core.Logging;

namespace DWS.Core.Policy
{
    public class PolicyEntry
    {
        public string Key { get; set; }
        public string ValueName { get; set; }
        public uint Type { get; set; }
        public byte[] Data { get; set; }
    }

    /// <summary>
    /// Local Group Policy Object (LGPO) registry.pol writer.
    /// Directly constructs genuine Windows registry.pol binary policy files to ensure
    /// system and user policies persist across Windows Updates and background policy refreshes.
    /// </summary>
    public sealed class LGPOWriter
    {
        private const uint REG_SZ = 1;
        private const uint REG_DWORD = 4;

        private static readonly byte[] PolHeader = new byte[] { 0x50, 0x52, 0x65, 0x67, 0x01, 0x00, 0x00, 0x00 }; // "PReg" + Version 1
        private static readonly byte[] OpenBracket = Encoding.Unicode.GetBytes("[");
        private static readonly byte[] Semicolon = Encoding.Unicode.GetBytes(";");
        private static readonly byte[] CloseBracket = Encoding.Unicode.GetBytes("]");

        private readonly List<PolicyEntry> _entries = new List<PolicyEntry>();

        public event CoreMessageEventHandler OnPolicyApplied;
        public event CoreErrorEventHandler OnPolicyError;

        public void SetDwordPolicy(string keyPath, string valueName, int value)
        {
            if (string.IsNullOrEmpty(keyPath)) throw new ArgumentNullException(nameof(keyPath));

            // Strip HKLM\ or SOFTWARE\ prefix if present to standardize on standard machine policy paths
            string normalizedKey = NormalizeKey(keyPath);
            byte[] dataBytes = BitConverter.GetBytes(value);

            _entries.Add(new PolicyEntry
            {
                Key = normalizedKey,
                ValueName = valueName ?? string.Empty,
                Type = REG_DWORD,
                Data = dataBytes
            });

            TransactionJournal.Log(JournalEntryType.PolicyOperation, $"LGPO Queue DWORD: {normalizedKey}\\{valueName} = {value}");
        }

        public void SetStringPolicy(string keyPath, string valueName, string value)
        {
            if (string.IsNullOrEmpty(keyPath)) throw new ArgumentNullException(nameof(keyPath));

            string normalizedKey = NormalizeKey(keyPath);
            byte[] dataBytes = Encoding.Unicode.GetBytes((value ?? string.Empty) + "\0");

            _entries.Add(new PolicyEntry
            {
                Key = normalizedKey,
                ValueName = valueName ?? string.Empty,
                Type = REG_SZ,
                Data = dataBytes
            });

            TransactionJournal.Log(JournalEntryType.PolicyOperation, $"LGPO Queue String: {normalizedKey}\\{valueName} = '{value}'");
        }

        public void SaveAndApplyMachinePolicy()
        {
            try
            {
                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string machineDir = Path.Combine(windir, @"System32\GroupPolicy\Machine");
                if (!Directory.Exists(machineDir))
                {
                    Directory.CreateDirectory(machineDir);
                }

                string polPath = Path.Combine(machineDir, "registry.pol");
                WritePolFile(polPath);

                TransactionJournal.Log(JournalEntryType.PolicyOperation, $"registry.pol binary written: {polPath}. Triggering gpupdate /force...");

                // Execute gpupdate /force
                using (var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "gpupdate.exe",
                        Arguments = "/force",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                })
                {
                    proc.Start();
                    proc.WaitForExit(15000);
                    TransactionJournal.Log(JournalEntryType.PolicyOperation, $"gpupdate finished with exit code {proc.ExitCode}");
                    OnPolicyApplied?.Invoke(this, $"LGPO policies applied successfully (gpupdate code {proc.ExitCode})");
                }
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, "Failed to apply LGPO machine policy", ex);
                OnPolicyError?.Invoke(this, ex);
                throw;
            }
        }

        private void WritePolFile(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var bw = new BinaryWriter(fs))
            {
                // Write Header: PReg + 1
                bw.Write(PolHeader);

                foreach (var entry in _entries)
                {
                    // Format: [Key;Value;Type;Size;Data]
                    bw.Write(OpenBracket);

                    bw.Write(Encoding.Unicode.GetBytes(entry.Key + "\0"));
                    bw.Write(Semicolon);

                    bw.Write(Encoding.Unicode.GetBytes(entry.ValueName + "\0"));
                    bw.Write(Semicolon);

                    bw.Write(entry.Type);
                    bw.Write(Semicolon);

                    bw.Write((uint)entry.Data.Length);
                    bw.Write(Semicolon);

                    bw.Write(entry.Data);

                    bw.Write(CloseBracket);
                }
            }
        }

        private static string NormalizeKey(string key)
        {
            string k = key.Trim();
            if (k.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)) k = k.Substring(5);
            if (k.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase)) k = k.Substring(19);
            return k;
        }
    }
}
