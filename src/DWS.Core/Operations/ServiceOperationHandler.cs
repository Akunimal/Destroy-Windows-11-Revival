using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DWS.Core.KTM;
using DWS.Core.Security;
using Microsoft.Win32;

namespace DWS.Core.Operations;

public sealed partial class ServiceOperationHandler
{
    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SERVICE_QUERY_CONFIG = 0x0001;
    public const uint SERVICE_CHANGE_CONFIG = 0x0002;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_STOP = 0x0020;
    public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;

    public const uint SERVICE_AUTO_START = 2;
    public const uint SERVICE_DEMAND_START = 3; // Manual
    public const uint SERVICE_DISABLED = 4;
    public const uint SERVICE_CONTROL_STOP = 1;

    public const int ERROR_ACCESS_DENIED = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct QUERY_SERVICE_CONFIGW
    {
        public uint dwServiceType;
        public uint dwStartType;
        public uint dwErrorControl;
        public IntPtr lpBinaryPathName;
        public IntPtr lpLoadOrderGroup;
        public uint dwTagId;
        public IntPtr lpDependencies;
        public IntPtr lpServiceStartName;
        public IntPtr lpDisplayName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceConfig(IntPtr hService, IntPtr lpServiceConfig, uint cbBufSize, out uint pcbBytesNeeded);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceStatus", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfig(
        IntPtr hService,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string? lpBinaryPathName,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword,
        string? lpDisplayName);

    [LibraryImport("advapi32.dll", EntryPoint = "ControlService", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ControlService(IntPtr hService, uint dwControl, out SERVICE_STATUS lpServiceStatus);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr hSCObject);

    public (bool Success, uint StartType, bool IsRunning) QueryServiceState(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
            return (false, 0, false);

        IntPtr hScm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (hScm == IntPtr.Zero)
            return QueryServiceStateFromRegistry(serviceName);

        try
        {
            IntPtr hService = OpenService(hScm, serviceName, SERVICE_QUERY_CONFIG | SERVICE_QUERY_STATUS);
            if (hService == IntPtr.Zero)
                return QueryServiceStateFromRegistry(serviceName);

            try
            {
                bool isRunning = false;
                if (QueryServiceStatus(hService, out var status))
                {
                    isRunning = status.dwCurrentState == 4; // SERVICE_RUNNING
                }

                // Initial probe to get required buffer size
                QueryServiceConfig(hService, IntPtr.Zero, 0, out uint bytesNeeded);
                if (bytesNeeded == 0)
                    return QueryServiceStateFromRegistry(serviceName);

                unsafe
                {
                    void* buffer = NativeMemory.Alloc(bytesNeeded);
                    try
                    {
                        if (QueryServiceConfig(hService, (IntPtr)buffer, bytesNeeded, out _))
                        {
                            var config = Marshal.PtrToStructure<QUERY_SERVICE_CONFIGW>((IntPtr)buffer);
                            return (true, config.dwStartType, isRunning);
                        }
                    }
                    finally
                    {
                        NativeMemory.Free(buffer);
                    }
                }

                return QueryServiceStateFromRegistry(serviceName);
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

    public bool ConfigureServiceStartType(string serviceName, uint newStartType, KtmTransactionManager? ktm = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        IntPtr hScm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (hScm != IntPtr.Zero)
        {
            try
            {
                IntPtr hService = OpenService(hScm, serviceName, SERVICE_CHANGE_CONFIG | SERVICE_STOP);
                if (hService != IntPtr.Zero)
                {
                    try
                    {
                        if (ChangeServiceConfig(
                            hService,
                            SERVICE_NO_CHANGE,
                            newStartType,
                            SERVICE_NO_CHANGE,
                            null, null, IntPtr.Zero, null, null, null, null))
                        {
                            if (newStartType == SERVICE_DISABLED)
                            {
                                // Gracefully attempt to stop service (ignore error if PPL protected)
                                ControlService(hService, SERVICE_CONTROL_STOP, out _);
                            }
                            return true;
                        }

                        int lastErr = Marshal.GetLastPInvokeError();
                        if (lastErr != ERROR_ACCESS_DENIED)
                        {
                            return false;
                        }
                    }
                    finally
                    {
                        CloseServiceHandle(hService);
                    }
                }
            }
            finally
            {
                CloseServiceHandle(hScm);
            }
        }

        // PPL fallback: Modify HKLM\SYSTEM\CurrentControlSet\Services\<serviceName>\Start directly under TrustedInstaller
        try
        {
            string serviceRegPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
            string desiredVal = newStartType.ToString();

            SystemPrivilegeBroker.RunAsTrustedInstaller(() =>
            {
                if (ktm != null)
                {
                    ktm.SetValue("HKLM", serviceRegPath, "Start", desiredVal);
                    if (newStartType == SERVICE_DISABLED)
                    {
                        try { ktm.SetValue("HKLM", serviceRegPath, "UserServiceFlags", "0"); } catch { }
                    }
                }
                else
                {
                    using var key = Registry.LocalMachine.OpenSubKey(serviceRegPath, writable: true);
                    key?.SetValue("Start", (int)newStartType, RegistryValueKind.DWord);
                    if (newStartType == SERVICE_DISABLED && key?.GetValue("UserServiceFlags") != null)
                    {
                        try { key.SetValue("UserServiceFlags", 0, RegistryValueKind.DWord); } catch { }
                    }
                }
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (bool Success, uint StartType, bool IsRunning) QueryServiceStateFromRegistry(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", false);
            if (key == null) return (false, 0, false);

            var startVal = key.GetValue("Start");
            if (startVal is int startInt)
            {
                return (true, (uint)startInt, false);
            }
            return (false, 0, false);
        }
        catch
        {
            return (false, 0, false);
        }
    }

    public static uint ParseStartMode(string? startMode)
    {
        if (string.IsNullOrEmpty(startMode)) return SERVICE_DISABLED;
        return startMode.ToLowerInvariant() switch
        {
            "disabled" or "4" => SERVICE_DISABLED,
            "manual" or "demand" or "3" => SERVICE_DEMAND_START,
            "auto" or "automatic" or "2" => SERVICE_AUTO_START,
            _ => SERVICE_DISABLED
        };
    }
}
