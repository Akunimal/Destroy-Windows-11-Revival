using System.Diagnostics;
using DWS.Core.KTM;
using DWS.Core.Security;
using Microsoft.Win32;

namespace DWS.Core.Operations;

public sealed class AppxOperationHandler
{
    public const string DeprovisionedBaseKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Deprovisioned";
    public const string ApplicationsBaseKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications";

    public static List<string> DetectMatchingPackages(string packageFamilyPattern)
    {
        var matched = new List<string>();
        if (!OperatingSystem.IsWindows())
            return matched;

        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(ApplicationsBaseKey, false);
            if (baseKey != null)
            {
                foreach (string subKeyName in baseKey.GetSubKeyNames())
                {
                    if (subKeyName.Contains(packageFamilyPattern, StringComparison.OrdinalIgnoreCase))
                    {
                        matched.Add(subKeyName);
                    }
                }
            }
        }
        catch
        {
            // Non-fatal inspection
        }

        return matched;
    }

    public static async Task<bool> RemoveAndDeprovisionPackageAsync(
        string packageFamilyPattern,
        bool isDryRun,
        KtmTransactionManager? ktm = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyPattern))
            return false;

        string safePattern = SanitizePackageName(packageFamilyPattern);

        if (isDryRun)
        {
            // DryRun only queries installed/provisioned packages without changes
            var packages = DetectMatchingPackages(safePattern);
            return packages.Count > 0;
        }

        if (!OperatingSystem.IsWindows())
            return false;

        // 1. Mark as Deprovisioned in Registry to prevent Windows Update from re-downloading
        // Resolve exact PackageFamilyName (Name_PublisherId) to prevent wildcard collision in Deprovisioned store
        var matchedPackages = DetectMatchingPackages(safePattern);
        var familiesToDeprovision = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pkgFullName in matchedPackages)
        {
            var parts = pkgFullName.Split('_');
            if (parts.Length >= 2)
            {
                familiesToDeprovision.Add($"{parts[0]}_{parts[^1]}");
            }
        }

        string cleanPattern = safePattern.Trim('*');
        if (!string.IsNullOrWhiteSpace(cleanPattern))
        {
            familiesToDeprovision.Add(cleanPattern);
        }

        foreach (var familyName in familiesToDeprovision)
        {
            string deprovSubKey = $@"{DeprovisionedBaseKey}\{familyName}";
            try
            {
                SystemPrivilegeBroker.RunAsTrustedInstaller(() =>
                {
                    if (ktm != null)
                    {
                        ktm.SetValue("HKLM", deprovSubKey, "IsDeprovisioned", "1");
                    }
                    else
                    {
                        using var key = Registry.LocalMachine.CreateSubKey(deprovSubKey, writable: true);
                        key?.SetValue("IsDeprovisioned", 1, RegistryValueKind.DWord);
                    }
                });
            }
            catch
            {
                // Non-fatal if deprovision key creation fails
            }
        }

        // 2. Execute AllUsers and Provisioned Package Removal via isolated PowerShell subprocess
        string psCommand =
            $"Get-AppxPackage -AllUsers -Name '*{safePattern}*' | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue; " +
            $"Get-AppxProvisionedPackage -Online | Where-Object PackageName -like '*{safePattern}*' | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue";

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{psCommand}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
                return proc.ExitCode == 0;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static bool RevertDeprovisioning(string packageFamilyPattern)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        string safePattern = SanitizePackageName(packageFamilyPattern);
        string deprovSubKey = $@"{DeprovisionedBaseKey}\{safePattern}";

        try
        {
            SystemPrivilegeBroker.RunAsTrustedInstaller(() =>
            {
                using var key = Registry.LocalMachine.OpenSubKey(DeprovisionedBaseKey, writable: true);
                key?.DeleteSubKeyTree(safePattern, throwOnMissingSubKey: false);
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string SanitizePackageName(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
