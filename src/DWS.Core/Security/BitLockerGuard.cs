using System.Diagnostics;

namespace DWS.Core.Security;

public enum BitLockerStatus
{
    Protected,
    Unprotected,
    NotAvailable
}

public sealed class BitLockerGuard : IDisposable
{
    private readonly string _driveLetter;
    private bool _suspended;
    private bool _rebootConfirmed;
    private bool _disposed;

    private BitLockerGuard(string driveLetter)
    {
        _driveLetter = driveLetter.TrimEnd('\\');
    }

    public static async Task<string> GetProtectionStatusAsync(string driveLetter)
    {
        var status = await GetStatusAsync(driveLetter);
        return status switch
        {
            BitLockerStatus.Protected => "Protected (On)",
            BitLockerStatus.Unprotected => "Unprotected (Off)",
            _ => "NotAvailable"
        };
    }

    public static async Task<BitLockerStatus> GetStatusAsync(string driveLetter)
    {
        string drive = driveLetter.TrimEnd('\\');
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "manage-bde.exe",
                Arguments = $"-status {drive}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return BitLockerStatus.NotAvailable;

            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (output.Contains("Protection On", StringComparison.OrdinalIgnoreCase))
            {
                return BitLockerStatus.Protected;
            }
            if (output.Contains("Protection Off", StringComparison.OrdinalIgnoreCase))
            {
                return BitLockerStatus.Unprotected;
            }

            return BitLockerStatus.NotAvailable;
        }
        catch
        {
            return BitLockerStatus.NotAvailable;
        }
    }

    public static BitLockerGuard ProtectSession(string driveLetter = "C:", int rebootCount = 1)
    {
        var guard = new BitLockerGuard(driveLetter);
        guard.SuspendProtection(rebootCount);
        return guard;
    }

    public void ConfirmRebootPending()
    {
        _rebootConfirmed = true;
    }

    private void SuspendProtection(int rebootCount)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "manage-bde.exe",
                Arguments = $"-protectors -disable {_driveLetter} -rebootcount {rebootCount}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            if (proc?.ExitCode == 0)
            {
                _suspended = true;
            }
        }
        catch
        {
            // BitLocker not installed or volume not encrypted
        }
    }

    private void ResumeProtection()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "manage-bde.exe",
                Arguments = $"-protectors -enable {_driveLetter}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            _suspended = false;
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_suspended && !_rebootConfirmed)
        {
            ResumeProtection();
        }
    }
}
