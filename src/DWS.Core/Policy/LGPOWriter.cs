using System.Diagnostics;
using System.Text;

namespace DWS.Core.Policy;

public sealed class LGPOWriter
{
    private const uint PREG_SIGNATURE = 0x67655250; // 'PReg'
    private const uint PREG_VERSION = 1;

    // Microsoft [MS-GPOL] Section 2.2.1.1.1 Registry Policy File Format specifies
    // that delimiters between fields are 2-byte UTF-16LE characters: '[' (0x005B), ';' (0x003B), ']' (0x005D).
    private static readonly byte[] OpenBracket = [0x5B, 0x00];
    private static readonly byte[] Semicolon = [0x3B, 0x00];
    private static readonly byte[] CloseBracket = [0x5D, 0x00];

    private readonly List<PolicyEntry> _entries = new();

    private sealed record PolicyEntry(string Key, string Value, uint Type, byte[] Data);

    public int EntryCount => _entries.Count;

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

    public void SavePolicy(string polPath)
    {
        string? dir = Path.GetDirectoryName(polPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = new FileStream(polPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        // Write PReg Header
        bw.Write(PREG_SIGNATURE);
        bw.Write(PREG_VERSION);

        foreach (var entry in _entries)
        {
            // Format: [Key;Value;Type;Size;Data] encoded in UTF-16LE delimiters
            bw.Write(OpenBracket);
            bw.Write(Encoding.Unicode.GetBytes(entry.Key + "\0"));
            bw.Write(Semicolon);
            bw.Write(Encoding.Unicode.GetBytes(entry.Value + "\0"));
            bw.Write(Semicolon);
            bw.Write(entry.Type);
            bw.Write(Semicolon);
            bw.Write((uint)entry.Data.Length);
            bw.Write(Semicolon);
            bw.Write(entry.Data);
            bw.Write(CloseBracket);
        }
    }

    public void SaveAndApplyMachinePolicy()
    {
        string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string gpoMachineDir = Path.Combine(systemRoot, @"System32\GroupPolicy\Machine");
        string polPath = Path.Combine(gpoMachineDir, "registry.pol");

        SavePolicy(polPath);

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
