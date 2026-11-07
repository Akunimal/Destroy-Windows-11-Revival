using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DWS.Core.Security;

public sealed partial class SystemPrivilegeBroker
{
    public const string WatchdogTaskName = "DWS_EnterpriseBaseline_Watchdog";
    public const string TrustedInstallerServiceName = "TrustedInstaller";

    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_RUNNING = 0x00000004;
    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenImpersonation = 2;
    private const int SC_STATUS_PROCESS_INFO = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out SafeAccessTokenHandle TokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out LUID lpLuid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        SafeAccessTokenHandle TokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
        in TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatusEx(
        IntPtr hService,
        int InfoLevel,
        out SERVICE_STATUS_PROCESS lpBuffer,
        uint cbBufSize,
        out uint pcbBytesNeeded);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr hSCObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateTokenEx(
        SafeAccessTokenHandle hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int ImpersonationLevel,
        int TokenType,
        out SafeAccessTokenHandle phNewToken);

    // ─────────────────────────────────────────────────────────────────────────
    // Privilege Elevation & Verification
    // ─────────────────────────────────────────────────────────────────────────

    public bool IsRunningAsSystem()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.IsSystem;
        }
        catch
        {
            return false;
        }
    }

    public bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool EnablePrivilege(string privilegeName, bool enable = true)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var tokenHandle))
            {
                return false;
            }

            using (tokenHandle)
            {
                if (!LookupPrivilegeValueW(null, privilegeName, out var luid))
                {
                    return false;
                }

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = enable ? SE_PRIVILEGE_ENABLED : 0
                    }
                };

                bool success = AdjustTokenPrivileges(tokenHandle, false, in tp, 0, IntPtr.Zero, IntPtr.Zero);
                return success && Marshal.GetLastPInvokeError() != 1300; // 1300 = ERROR_NOT_ALL_ASSIGNED
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool EnableProcessPrivileges()
    {
        bool dbg = EnablePrivilege("SeDebugPrivilege", true);
        bool imp = EnablePrivilege("SeImpersonatePrivilege", true);
        bool take = EnablePrivilege("SeTakeOwnershipPrivilege", true);
        return dbg || imp || take;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TrustedInstaller Token Acquisition & Impersonation
    // ─────────────────────────────────────────────────────────────────────────

    public static SafeAccessTokenHandle CreateTrustedInstallerToken()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("TrustedInstaller elevation is only supported on Windows.");

        // 1. Enable SeDebugPrivilege and SeImpersonatePrivilege
        EnableProcessPrivileges();

        // 2. Open SCM and get TrustedInstaller service
        IntPtr hScm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (hScm == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open Service Control Manager.");

        try
        {
            IntPtr hService = OpenService(hScm, TrustedInstallerServiceName, SERVICE_START | SERVICE_QUERY_STATUS);
            if (hService == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open TrustedInstaller service.");

            try
            {
                // Query status
                if (!QueryServiceStatusEx(hService, SC_STATUS_PROCESS_INFO, out var status, (uint)Marshal.SizeOf<SERVICE_STATUS_PROCESS>(), out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to query TrustedInstaller service status.");

                // If stopped, start service
                if (status.dwCurrentState != SERVICE_RUNNING)
                {
                    if (!StartService(hService, 0, IntPtr.Zero))
                    {
                        int err = Marshal.GetLastPInvokeError();
                        // 1056 = ERROR_SERVICE_ALREADY_RUNNING
                        if (err != 0 && err != 1056)
                        {
                            // Continue to status polling as CBS or another process might have started it
                        }
                    }

                    // Poll up to 5 seconds with 50ms backoff
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 5000)
                    {
                        Thread.Sleep(50);
                        if (QueryServiceStatusEx(hService, SC_STATUS_PROCESS_INFO, out status, (uint)Marshal.SizeOf<SERVICE_STATUS_PROCESS>(), out _)
                            && status.dwCurrentState == SERVICE_RUNNING
                            && status.dwProcessId != 0)
                        {
                            break;
                        }
                    }

                    if (status.dwCurrentState != SERVICE_RUNNING || status.dwProcessId == 0)
                        throw new TimeoutException("Timed out waiting for TrustedInstaller service to start.");
                }

                uint tiPid = status.dwProcessId;

                // 3. Open TrustedInstaller process
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, tiPid);
                if (hProcess == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open TrustedInstaller process (PID {tiPid}).");

                try
                {
                    // 4. Open its token
                    if (!OpenProcessToken(hProcess, TOKEN_DUPLICATE | TOKEN_QUERY, out var hTiToken))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open TrustedInstaller process token.");

                    using (hTiToken)
                    {
                        // 5. Duplicate token for impersonation
                        if (!DuplicateTokenEx(hTiToken, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenImpersonation, out var hDupToken))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to duplicate TrustedInstaller token.");

                        return hDupToken;
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            finally
            {
                CloseServiceHandle(hService);
            }
        }
        finally
        {
            CloseServiceHandle(hScm);
        }
    }

    /// <summary>
    /// Executes the specified action impersonating NT SERVICE\TrustedInstaller.
    /// Thread-safe and AOT-safe with automatic ExecutionContext propagation via WindowsIdentity.RunImpersonated.
    /// </summary>
    public static void RunAsTrustedInstaller(Action action)
    {
        using var tiToken = CreateTrustedInstallerToken();
        WindowsIdentity.RunImpersonated(tiToken, action);
    }

    /// <summary>
    /// Executes the specified function impersonating NT SERVICE\TrustedInstaller.
    /// </summary>
    public static T RunAsTrustedInstaller<T>(Func<T> function)
    {
        using var tiToken = CreateTrustedInstallerToken();
        return WindowsIdentity.RunImpersonated(tiToken, function);
    }

    /// <summary>
    /// Executes the specified asynchronous function impersonating NT SERVICE\TrustedInstaller.
    /// </summary>
    public static Task RunAsTrustedInstallerAsync(Func<Task> function)
    {
        using var tiToken = CreateTrustedInstallerToken();
        return WindowsIdentity.RunImpersonatedAsync(tiToken, function);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Watchdog Task Management
    // ─────────────────────────────────────────────────────────────────────────

    private const string WatchdogXmlTemplate =
"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>DWS Enterprise Baseline Watchdog - Monitors Windows Update events to ensure optimization integrity.</Description>
    <Author>DWS Enterprise</Author>
  </RegistrationInfo>
  <Triggers>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>&lt;QueryList&gt;&lt;Query Id="0" Path="Microsoft-Windows-WindowsUpdateClient/Operational"&gt;&lt;Select Path="Microsoft-Windows-WindowsUpdateClient/Operational"&gt;*[System[(EventID=19)]]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;</Subscription>
    </EventTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{0}</Command>
      <Arguments>/SILENT-AUDIT</Arguments>
    </Exec>
  </Actions>
</Task>
""";

    public bool InstallWatchdog(out string message)
    {
        message = string.Empty;
        string currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "DWS.Worker.exe";
        string tempXmlPath = Path.Combine(Path.GetTempPath(), $"DWS_Watchdog_{Guid.NewGuid():N}.xml");

        try
        {
            string xmlContent = string.Format(WatchdogXmlTemplate, EscapeXml(currentExe));
            File.WriteAllText(tempXmlPath, xmlContent, Encoding.Unicode);

            string createArgs = $"/create /tn \"{WatchdogTaskName}\" /xml \"{tempXmlPath}\" /f";
            int exitCode = RunHiddenProcess("schtasks.exe", createArgs, 10000);

            if (exitCode == 0)
            {
                message = "Watchdog task installed successfully (Trigger: Windows Update Event ID 19).";
                return true;
            }
            else
            {
                message = $"schtasks returned error code {exitCode}";
                return false;
            }
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(tempXmlPath)) File.Delete(tempXmlPath); } catch { }
        }
    }

    public bool InstallWatchdog() => InstallWatchdog(out _);

    public bool UninstallWatchdog(out string message)
    {
        message = string.Empty;
        try
        {
            string deleteArgs = $"/delete /tn \"{WatchdogTaskName}\" /f";
            int exitCode = RunHiddenProcess("schtasks.exe", deleteArgs, 10000);
            if (exitCode == 0)
            {
                message = "Watchdog task deleted successfully.";
                return true;
            }
            else
            {
                message = $"schtasks returned error code {exitCode}";
                return false;
            }
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public bool UninstallWatchdog() => UninstallWatchdog(out _);

    public bool IsWatchdogInstalled()
    {
        try
        {
            int exitCode = RunHiddenProcess("schtasks.exe", $"/query /tn \"{WatchdogTaskName}\"", 5000);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public string? GetWatchdogLastRunTime()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{WatchdogTaskName}\" /fo CSV /nh",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return output.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeXml(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
    }

    private static int RunHiddenProcess(string fileName, string args, int timeoutMs)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        proc.Start();
        if (!proc.WaitForExit(timeoutMs))
        {
            proc.Kill();
            throw new TimeoutException($"Process {fileName} {args} timed out after {timeoutMs}ms");
        }
        return proc.ExitCode;
    }
}
