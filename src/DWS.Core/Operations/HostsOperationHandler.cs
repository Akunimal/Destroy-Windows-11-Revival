using System.Runtime.InteropServices;
using System.Text;

namespace DWS.Core.Operations;

public sealed partial class HostsOperationHandler
{
    public const string DefaultHostsPath = @"C:\Windows\System32\drivers\etc\hosts";
    public const string BeginMarker = "# >>> DWS11-ENTERPRISE-MANAGED-BLOCK-START >>>";
    public const string EndMarker   = "# <<< DWS11-ENTERPRISE-MANAGED-BLOCK-END <<<";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsFlushResolverCache();

    public static void FlushDnsCache()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                DnsFlushResolverCache();
            }
            catch
            {
                // Non-critical if DNS cache flush API cannot be invoked
            }
        }
    }

    public static (bool HasBlock, string? BlockContent) ReadManagedBlock(string? hostsPath = null)
    {
        string path = hostsPath ?? DefaultHostsPath;
        if (!File.Exists(path))
            return (false, null);

        string content = File.ReadAllText(path, Encoding.UTF8);
        int startIndex = content.IndexOf(BeginMarker, StringComparison.OrdinalIgnoreCase);
        int endIndex = content.IndexOf(EndMarker, StringComparison.OrdinalIgnoreCase);

        if (startIndex >= 0 && endIndex > startIndex)
        {
            int blockEnd = endIndex + EndMarker.Length;
            string block = content.Substring(startIndex, blockEnd - startIndex);
            return (true, block);
        }

        return (false, null);
    }

    public static async Task<string?> ApplyHostsBlockAsync(
        IEnumerable<string> domainsToBlock,
        string? hostsPath = null,
        CancellationToken ct = default)
    {
        string path = hostsPath ?? DefaultHostsPath;
        string originalContent = File.Exists(path)
            ? await File.ReadAllTextAsync(path, Encoding.UTF8, ct)
            : "# Copyright (c) 1993-2009 Microsoft Corp.\r\n127.0.0.1 localhost\r\n::1 localhost\r\n";

        var (hasBlock, previousBlock) = ReadManagedBlock(path);

        // Remove any existing managed block (idempotency)
        string cleanedContent = RemoveManagedBlock(originalContent);

        // Build new managed block with deterministic sorting and explicit CRLF
        var sb = new StringBuilder();
        sb.Append(cleanedContent.TrimEnd());
        sb.Append("\r\n\r\n");
        sb.Append(BeginMarker);
        sb.Append("\r\n");

        foreach (var domain in domainsToBlock.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Distinct())
        {
            if (!string.IsNullOrWhiteSpace(domain))
            {
                sb.Append("0.0.0.0 ");
                sb.Append(domain.Trim());
                sb.Append("\r\n");
            }
        }

        sb.Append(EndMarker);
        sb.Append("\r\n");

        string newContent = sb.ToString();

        // Atomic write with retry for sharing violations with Dnscache service
        await WriteFileWithRetryAsync(path, newContent, ct);

        // Flush DNS resolver
        FlushDnsCache();

        return previousBlock;
    }

    public static async Task<bool> RemoveHostsBlockAsync(string? hostsPath = null, CancellationToken ct = default)
    {
        string path = hostsPath ?? DefaultHostsPath;
        if (!File.Exists(path))
            return false;

        string content = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        if (!content.Contains(BeginMarker, StringComparison.OrdinalIgnoreCase))
            return false;

        string cleaned = RemoveManagedBlock(content).TrimEnd() + "\r\n";
        await WriteFileWithRetryAsync(path, cleaned, ct);

        FlushDnsCache();
        return true;
    }

    public static string RemoveManagedBlock(string content)
    {
        int startIndex = content.IndexOf(BeginMarker, StringComparison.OrdinalIgnoreCase);
        int endIndex = content.IndexOf(EndMarker, StringComparison.OrdinalIgnoreCase);

        if (startIndex >= 0 && endIndex > startIndex)
        {
            int blockEnd = endIndex + EndMarker.Length;
            string before = content[..startIndex];
            string after = content[blockEnd..];
            return (before + after).TrimEnd() + "\r\n";
        }

        return content;
    }

    private static async Task WriteFileWithRetryAsync(string destinationPath, string content, CancellationToken ct)
    {
        string dir = Path.GetDirectoryName(destinationPath) ?? "";
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tempPath = destinationPath + ".dws_tmp";

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await File.WriteAllTextAsync(tempPath, content, Utf8NoBom, ct);
                File.Move(tempPath, destinationPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(50 * attempt, ct);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }
}
