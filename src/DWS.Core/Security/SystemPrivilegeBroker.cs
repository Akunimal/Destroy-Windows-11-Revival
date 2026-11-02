using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace DWS.Core.Security;

public sealed class SystemPrivilegeBroker
{
    public const string WatchdogTaskName = "DWS_EnterpriseBaseline_Watchdog";

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
