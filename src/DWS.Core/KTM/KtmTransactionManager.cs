using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DWS.Core.KTM;

public sealed partial class KtmTransactionManager : IDisposable
{
    public static readonly IntPtr HKEY_LOCAL_MACHINE = new(unchecked((int)0x80000002));
    public static readonly IntPtr HKEY_CURRENT_USER = new(unchecked((int)0x80000001));

    private const int ERROR_FILE_NOT_FOUND = 2;
    private const uint KEY_QUERY_VALUE = 0x0001;
    private const uint KEY_SET_VALUE = 0x0002;
    private const uint KEY_CREATE_SUB_KEY = 0x0004;
    private const uint KEY_ENUMERATE_SUB_KEYS = 0x0008;
    private const uint KEY_NOTIFY = 0x0010;
    private const uint STANDARD_RIGHTS_READ = 0x00020000;
    private const uint STANDARD_RIGHTS_WRITE = 0x00020000;
    private const uint KEY_WOW64_64KEY = 0x0100;

    // 0x20006 | 0x0100 = 0x20106: Explicit 64-bit view + write access (without WRITE_DAC/WRITE_OWNER)
    private const uint DEFAULT_WRITE_SAM = STANDARD_RIGHTS_WRITE | KEY_SET_VALUE | KEY_CREATE_SUB_KEY | KEY_WOW64_64KEY;

    // 0x20107: Explicit 64-bit view + read & write access
    private const uint DEFAULT_READ_WRITE_SAM = DEFAULT_WRITE_SAM | KEY_QUERY_VALUE;

    // 0x0102: Explicit 64-bit view + set/delete value only
    private const uint DELETE_VALUE_SAM = KEY_SET_VALUE | KEY_WOW64_64KEY;

    private const uint REG_DWORD = 4;
    private const uint REG_SZ = 1;

    private readonly SafeTransactionHandle _transactionHandle;
    private bool _committed;
    private bool _disposed;

    public SafeTransactionHandle TransactionHandle => _transactionHandle;
    public string Description { get; }

    public KtmTransactionManager(string description = "DWS_KtmTransaction")
    {
        Description = description;
        _transactionHandle = CreateTransaction(
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            description);

        if (_transactionHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create KTM transaction.");
        }
    }

    [LibraryImport("ktmw32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeTransactionHandle CreateTransaction(
        IntPtr securityAttributes,
        IntPtr guid,
        uint createOptions,
        uint isolationLevel,
        uint isolationFlags,
        uint timeout,
        string description);

    [LibraryImport("ktmw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CommitTransaction(SafeTransactionHandle transactionHandle);

    [LibraryImport("ktmw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RollbackTransaction(SafeTransactionHandle transactionHandle);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegCreateKeyTransactedW(
        IntPtr hKey,
        string lpSubKey,
        uint reserved,
        IntPtr lpClass,
        uint dwOptions,
        uint samDesired,
        IntPtr lpSecurityAttributes,
        out SafeRegistryHandle phkResult,
        out uint lpdwDisposition,
        SafeTransactionHandle hTransaction,
        IntPtr pExtendedOptions);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegOpenKeyTransactedW(
        IntPtr hKey,
        string lpSubKey,
        uint ulOptions,
        uint samDesired,
        out SafeRegistryHandle phkResult,
        SafeTransactionHandle hTransaction,
        IntPtr pExtendedOptions);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegCreateKeyExW(
        IntPtr hKey,
        string lpSubKey,
        uint reserved,
        IntPtr lpClass,
        uint dwOptions,
        uint samDesired,
        IntPtr lpSecurityAttributes,
        out SafeRegistryHandle phkResult,
        out uint lpdwDisposition);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegOpenKeyExW(
        IntPtr hKey,
        string lpSubKey,
        uint ulOptions,
        uint samDesired,
        out SafeRegistryHandle phkResult);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegSetValueExW(
        SafeRegistryHandle hKey,
        string lpValueName,
        uint reserved,
        uint dwType,
        byte[] lpData,
        uint cbData);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegDeleteValueW(
        SafeRegistryHandle hKey,
        string lpValueName);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegDeleteKeyExW(
        IntPtr hKey,
        string lpSubKey,
        uint samDesired,
        uint reserved);

    public void SetDword(IntPtr hRootKey, string subKeyPath, string valueName, int data)
    {
        ThrowIfDisposedOrCommitted();

        int error = RegCreateKeyTransactedW(
            hRootKey,
            subKeyPath,
            0,
            IntPtr.Zero,
            0,
            DEFAULT_WRITE_SAM,
            IntPtr.Zero,
            out SafeRegistryHandle hKeyResult,
            out _,
            _transactionHandle,
            IntPtr.Zero);

        if (error != 0)
        {
            throw new Win32Exception(error, $"Failed to open/create transacted key '{subKeyPath}'.");
        }

        using (hKeyResult)
        {
            byte[] bytes = BitConverter.GetBytes(data);
            error = RegSetValueExW(hKeyResult, valueName, 0, REG_DWORD, bytes, (uint)bytes.Length);
            if (error != 0)
            {
                throw new Win32Exception(error, $"Failed to set transacted DWORD value '{valueName}'.");
            }
        }
    }

    public void SetString(IntPtr hRootKey, string subKeyPath, string valueName, string data)
    {
        ThrowIfDisposedOrCommitted();

        int error = RegCreateKeyTransactedW(
            hRootKey,
            subKeyPath,
            0,
            IntPtr.Zero,
            0,
            DEFAULT_WRITE_SAM,
            IntPtr.Zero,
            out SafeRegistryHandle hKeyResult,
            out _,
            _transactionHandle,
            IntPtr.Zero);

        if (error != 0)
        {
            throw new Win32Exception(error, $"Failed to open/create transacted key '{subKeyPath}'.");
        }

        using (hKeyResult)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(data + "\0");
            error = RegSetValueExW(hKeyResult, valueName, 0, REG_SZ, bytes, (uint)bytes.Length);
            if (error != 0)
            {
                throw new Win32Exception(error, $"Failed to set transacted String value '{valueName}'.");
            }
        }
    }

    public void SetValue(string hive, string keyPath, string valueName, object value)
    {
        IntPtr hRoot = ResolveHive(hive);
        if (value is int intVal)
        {
            SetDword(hRoot, keyPath, valueName, intVal);
        }
        else if (int.TryParse(value?.ToString(), out int parsedInt))
        {
            SetDword(hRoot, keyPath, valueName, parsedInt);
        }
        else
        {
            SetString(hRoot, keyPath, valueName, value?.ToString() ?? string.Empty);
        }
    }

    public void DeleteValue(string hive, string keyPath, string valueName)
    {
        ThrowIfDisposedOrCommitted();
        IntPtr hRoot = ResolveHive(hive);

        int error = RegOpenKeyTransactedW(
            hRoot,
            keyPath,
            0,
            DELETE_VALUE_SAM,
            out SafeRegistryHandle hKeyResult,
            _transactionHandle,
            IntPtr.Zero);

        if (error == ERROR_FILE_NOT_FOUND)
        {
            return; // Key does not exist, nothing to delete
        }

        if (error != 0)
        {
            throw new Win32Exception(error, $"Failed to open transacted key '{keyPath}' for deletion (Win32 Error {error}).");
        }

        using (hKeyResult)
        {
            int delErr = RegDeleteValueW(hKeyResult, valueName);
            if (delErr != 0 && delErr != ERROR_FILE_NOT_FOUND)
            {
                throw new Win32Exception(delErr, $"Failed to delete transacted value '{valueName}' in '{keyPath}' (Win32 Error {delErr}).");
            }
        }
    }

    public void Commit()
    {
        ThrowIfDisposedOrCommitted();
        if (!CommitTransaction(_transactionHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Kernel transaction commit failed.");
        }
        _committed = true;
    }

    public void Rollback()
    {
        if (_committed || _disposed || _transactionHandle.IsInvalid || _transactionHandle.IsClosed)
        {
            return;
        }

        RollbackTransaction(_transactionHandle);
    }

    public static IntPtr ResolveHive(string hive)
    {
        return string.Equals(hive, "HKCU", StringComparison.OrdinalIgnoreCase)
            ? HKEY_CURRENT_USER
            : HKEY_LOCAL_MACHINE;
    }

    public static bool CanUseTxR(string hive, string subKeyPath, bool isImpersonatingTi = false)
    {
        if (isImpersonatingTi) return false; // Never mix thread impersonation with KTM handles
        if (string.Equals(hive, "HKCU", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(hive, "HKLM", StringComparison.OrdinalIgnoreCase))
        {
            string normalized = subKeyPath.TrimStart('\\');
            return normalized.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase) &&
                   !normalized.StartsWith(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule", StringComparison.OrdinalIgnoreCase);
        }
        return false; // HKLM\SYSTEM, HKLM\SAM, HKLM\SECURITY do not support TxR
    }

    public static void SetValueDirect(string hive, string subKeyPath, string valueName, object value)
    {
        IntPtr hRoot = ResolveHive(hive);
        int error = RegCreateKeyExW(
            hRoot,
            subKeyPath,
            0,
            IntPtr.Zero,
            0,
            DEFAULT_WRITE_SAM,
            IntPtr.Zero,
            out SafeRegistryHandle hKeyResult,
            out _);

        if (error != 0)
        {
            throw new Win32Exception(error, $"Failed to create/open key '{subKeyPath}' directly (Win32 Error {error}).");
        }

        using (hKeyResult)
        {
            if (value is int intVal || (value != null && int.TryParse(value.ToString(), out intVal)))
            {
                byte[] bytes = BitConverter.GetBytes(intVal);
                error = RegSetValueExW(hKeyResult, valueName, 0, REG_DWORD, bytes, (uint)bytes.Length);
            }
            else
            {
                byte[] bytes = Encoding.Unicode.GetBytes((value?.ToString() ?? string.Empty) + "\0");
                error = RegSetValueExW(hKeyResult, valueName, 0, REG_SZ, bytes, (uint)bytes.Length);
            }

            if (error != 0)
            {
                throw new Win32Exception(error, $"Failed to set direct value '{valueName}' in '{subKeyPath}' (Win32 Error {error}).");
            }
        }
    }

    public static void DeleteValueDirect(string hive, string subKeyPath, string valueName)
    {
        IntPtr hRoot = ResolveHive(hive);
        int error = RegOpenKeyExW(
            hRoot,
            subKeyPath,
            0,
            DELETE_VALUE_SAM,
            out SafeRegistryHandle hKeyResult);

        if (error == ERROR_FILE_NOT_FOUND) return;
        if (error != 0)
        {
            throw new Win32Exception(error, $"Failed to open key '{subKeyPath}' for direct deletion (Win32 Error {error}).");
        }

        using (hKeyResult)
        {
            int delErr = RegDeleteValueW(hKeyResult, valueName);
            if (delErr != 0 && delErr != ERROR_FILE_NOT_FOUND)
            {
                throw new Win32Exception(delErr, $"Failed to delete direct value '{valueName}' in '{subKeyPath}' (Win32 Error {delErr}).");
            }
        }
    }

    public static void DeleteKeyIfEmptyDirect(string hive, string subKeyPath)
    {
        try
        {
            var registryHive = string.Equals(hive, "HKCU", StringComparison.OrdinalIgnoreCase)
                ? RegistryHive.CurrentUser
                : RegistryHive.LocalMachine;

            using var baseKey = RegistryKey.OpenBaseKey(registryHive, RegistryView.Registry64);
            using (var subKey = baseKey.OpenSubKey(subKeyPath, false))
            {
                if (subKey == null || subKey.SubKeyCount > 0 || subKey.ValueCount > 0)
                {
                    return;
                }
            }

            IntPtr hRoot = ResolveHive(hive);
            RegDeleteKeyExW(hRoot, subKeyPath, KEY_WOW64_64KEY, 0);
        }
        catch
        {
            // Best-effort cleanup for empty keys
        }
    }

    private void ThrowIfDisposedOrCommitted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_committed)
        {
            throw new InvalidOperationException("Transaction has already been committed.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_committed && !_transactionHandle.IsInvalid && !_transactionHandle.IsClosed)
        {
            RollbackTransaction(_transactionHandle);
        }

        _transactionHandle.Dispose();
    }
}
