using System.Diagnostics;
using System.Text;

namespace DWS.Core.Policy;

public sealed class LGPOWriter
{
    private const uint PREG_SIGNATURE = 0x67655250; // 'PReg'
    private const uint PREG_VERSION = 1;

    private readonly List<PolicyEntry> _entries = new();

    private sealed record PolicyEntry(string Key, string Value, uint Type, byte[] Data);

    public void SetDwordPolicy(string keyPath, string valueName, int data)
    {
        string normalizedKey = keyPath.Replace('/', '\\').Trim('\\');
        byte[] raw = BitConverter.GetBytes(data);
        _entries.Add(new PolicyEntry(normalizedKey, valueName, 4, raw)); // 4 = REG_DWORD
    }

    public void SetStringPolicy(string keyPath, string valueName, string data)
    {
        string normalizedKey = keyPath.Replace('/', '\\').Trim('\\');
        byte[] raw = Encoding.Unicode.GetBytes(data + "\0");
        _entries.Add(new PolicyEntry(normalizedKey, valueName, 1, raw)); // 1 = REG_SZ
    }

    public void SaveAndApplyMachinePolicy()
    {
        string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string gpoMachineDir = Path.Combine(systemRoot, @"System32\GroupPolicy\Machine");
        Directory.CreateDirectory(gpoMachineDir);

        string polPath = Path.Combine(gpoMachineDir, "registry.pol");

        using (var fs = new FileStream(polPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var bw = new BinaryWriter(fs))
        {
            // Write PReg Header
            bw.Write(PREG_SIGNATURE);
            bw.Write(PREG_VERSION);

            foreach (var entry in _entries)
            {
                // Format: [Key;Value;Type;Size;Data]
                bw.Write('[');
                bw.Write(Encoding.Unicode.GetBytes(entry.Key + "\0"));
                bw.Write(';');
                bw.Write(Encoding.Unicode.GetBytes(entry.Value + "\0"));
                bw.Write(';');
                bw.Write(BitConverter.GetBytes(entry.Type));
                bw.Write(';');
                bw.Write(BitConverter.GetBytes((uint)entry.Data.Length));
                bw.Write(';');
                bw.Write(entry.Data);
                bw.Write(']');
            }
        }

        // Force Group Policy engine refresh
        var psi = new ProcessStartInfo
        {
            FileName = "gpupdate.exe",
            Arguments = "/target:computer /force",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(10000);
    }
}
