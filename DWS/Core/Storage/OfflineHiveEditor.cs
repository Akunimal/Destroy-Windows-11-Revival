using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DWS.Core.Logging;
using Microsoft.Win32.SafeHandles;

namespace DWS.Core.Storage
{
    /// <summary>
    /// Offline hive editor utilizing RegLoadAppKey.
    /// Loads user hives (such as C:\Users\Default\NTUSER.DAT) into an isolated private process namespace.
    /// Avoids polluting global HKEY_USERS and prevents file lock contentions from background antivirus engines.
    /// </summary>
    public sealed class OfflineHiveEditor : IDisposable
    {
        private const uint REG_PROCESS_APPKEY = 0x00000001;
        private const uint KEY_WRITE = 0x20006;
        private const uint KEY_READ = 0x20019;
        private const uint KEY_ALL_ACCESS = 0xF003F;

        private const uint REG_SZ = 1;
        private const uint REG_DWORD = 4;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegLoadAppKey(
            string lpFile,
            out SafeRegistryHandle phkResult,
            uint samDesired,
            uint dwOptions,
            uint Reserved);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegCreateKeyEx(
            SafeRegistryHandle hKey,
            string lpSubKey,
            uint Reserved,
            string lpClass,
            uint dwOptions,
            uint samDesired,
            IntPtr lpSecurityAttributes,
            out SafeRegistryHandle phkResult,
            out uint lpdwDisposition);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegSetValueEx(
            SafeRegistryHandle hKey,
            string lpValueName,
            uint Reserved,
            uint dwType,
            byte[] lpData,
            uint cbData);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegDeleteValue(
            SafeRegistryHandle hKey,
            string lpValueName);

        private SafeRegistryHandle _rootHiveHandle;
        private readonly string _hivePath;
        private bool _isDisposed;

        public string HivePath => _hivePath;
        public bool IsLoaded => _rootHiveHandle != null && !_rootHiveHandle.IsInvalid && !_isDisposed;

        public event CoreMessageEventHandler OnOperationSuccess;
        public event CoreErrorEventHandler OnOperationError;

        private OfflineHiveEditor(string hivePath, SafeRegistryHandle handle)
        {
            _hivePath = hivePath;
            _rootHiveHandle = handle;
            TransactionJournal.Log(JournalEntryType.Info, $"Isolated AppKey Hive loaded: {hivePath}");
        }

        public static OfflineHiveEditor OpenHive(string ntuserDatPath)
        {
            if (string.IsNullOrEmpty(ntuserDatPath))
            {
                throw new ArgumentNullException(nameof(ntuserDatPath));
            }

            if (!File.Exists(ntuserDatPath))
            {
                var ex = new FileNotFoundException($"Offline hive file not found: {ntuserDatPath}", ntuserDatPath);
                TransactionJournal.Log(JournalEntryType.Error, "Offline hive file missing", ex);
                throw ex;
            }

            SafeRegistryHandle handle;
            int result = RegLoadAppKey(ntuserDatPath, out handle, KEY_ALL_ACCESS, REG_PROCESS_APPKEY, 0);
            if (result != 0 || handle == null || handle.IsInvalid)
            {
                var ex = new Win32Exception(result, $"RegLoadAppKey failed for {ntuserDatPath} (Win32 Code {result})");
                TransactionJournal.Log(JournalEntryType.Error, $"RegLoadAppKey failed: {ntuserDatPath}", ex);
                throw ex;
            }

            return new OfflineHiveEditor(ntuserDatPath, handle);
        }

        public void SetDword(string subKey, string valueName, int value)
        {
            EnsureLoaded();
            SafeRegistryHandle hSubKey = null;
            try
            {
                uint disposition;
                int result = RegCreateKeyEx(
                    _rootHiveHandle,
                    subKey,
                    0,
                    null,
                    0,
                    KEY_WRITE,
                    IntPtr.Zero,
                    out hSubKey,
                    out disposition);

                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegCreateKeyEx failed in AppKey on {subKey} (Code {result})");
                }

                byte[] data = BitConverter.GetBytes(value);
                result = RegSetValueEx(hSubKey, valueName, 0, REG_DWORD, data, (uint)data.Length);
                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegSetValueEx failed in AppKey for {subKey}\\{valueName} (Code {result})");
                }

                string msg = $"AppKey [{Path.GetFileName(_hivePath)}] Set DWORD: {subKey}\\{valueName} = {value}";
                TransactionJournal.Log(JournalEntryType.RegistryOperation, msg);
                OnOperationSuccess?.Invoke(this, msg);
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, $"OfflineHive SetDword failed on {subKey}\\{valueName}", ex);
                OnOperationError?.Invoke(this, ex);
                throw;
            }
            finally
            {
                hSubKey?.Dispose();
            }
        }

        public void SetString(string subKey, string valueName, string value)
        {
            EnsureLoaded();
            SafeRegistryHandle hSubKey = null;
            try
            {
                uint disposition;
                int result = RegCreateKeyEx(
                    _rootHiveHandle,
                    subKey,
                    0,
                    null,
                    0,
                    KEY_WRITE,
                    IntPtr.Zero,
                    out hSubKey,
                    out disposition);

                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegCreateKeyEx failed in AppKey on {subKey} (Code {result})");
                }

                byte[] data = Encoding.Unicode.GetBytes((value ?? string.Empty) + "\0");
                result = RegSetValueEx(hSubKey, valueName, 0, REG_SZ, data, (uint)data.Length);
                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegSetValueEx failed in AppKey for {subKey}\\{valueName} (Code {result})");
                }

                string msg = $"AppKey [{Path.GetFileName(_hivePath)}] Set String: {subKey}\\{valueName} = '{value}'";
                TransactionJournal.Log(JournalEntryType.RegistryOperation, msg);
                OnOperationSuccess?.Invoke(this, msg);
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, $"OfflineHive SetString failed on {subKey}\\{valueName}", ex);
                OnOperationError?.Invoke(this, ex);
                throw;
            }
            finally
            {
                hSubKey?.Dispose();
            }
        }

        public void DeleteValue(string subKey, string valueName)
        {
            EnsureLoaded();
            SafeRegistryHandle hSubKey = null;
            try
            {
                uint disposition;
                int result = RegCreateKeyEx(
                    _rootHiveHandle,
                    subKey,
                    0,
                    null,
                    0,
                    KEY_WRITE,
                    IntPtr.Zero,
                    out hSubKey,
                    out disposition);

                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegCreateKeyEx failed in AppKey on {subKey} (Code {result})");
                }

                result = RegDeleteValue(hSubKey, valueName);
                if (result != 0 && result != 2) // 2 = ERROR_FILE_NOT_FOUND
                {
                    throw new Win32Exception(result, $"RegDeleteValue failed in AppKey for {subKey}\\{valueName} (Code {result})");
                }

                string msg = $"AppKey [{Path.GetFileName(_hivePath)}] Deleted Value: {subKey}\\{valueName}";
                TransactionJournal.Log(JournalEntryType.RegistryOperation, msg);
                OnOperationSuccess?.Invoke(this, msg);
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, $"OfflineHive DeleteValue failed on {subKey}\\{valueName}", ex);
                OnOperationError?.Invoke(this, ex);
                throw;
            }
            finally
            {
                hSubKey?.Dispose();
            }
        }

        private void EnsureLoaded()
        {
            if (!IsLoaded)
            {
                throw new InvalidOperationException("Offline hive is not loaded or has been disposed.");
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            if (_rootHiveHandle != null && !_rootHiveHandle.IsInvalid)
            {
                // Disposing the SafeRegistryHandle automatically unloads the AppKey from memory cleanly
                _rootHiveHandle.Dispose();
                _rootHiveHandle = null;
                TransactionJournal.Log(JournalEntryType.Info, $"Isolated AppKey Hive unloaded: {_hivePath}");
            }

            _isDisposed = true;
        }
    }
}
