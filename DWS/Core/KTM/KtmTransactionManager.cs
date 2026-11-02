using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DWS.Core.Logging;
using Microsoft.Win32.SafeHandles;

namespace DWS.Core.KTM
{
    /// <summary>
    /// Kernel Transaction Manager (KTM) controller.
    /// Manages atomic registry modifications using the native Windows Kernel Transaction Manager (KTM / TxF / TxR).
    /// Guarantees ACID properties: full commit on success or automatic rollback on failure/exception.
    /// </summary>
    public sealed class KtmTransactionManager : IDisposable
    {
        #region Native Constants & Types

        public static readonly IntPtr HKEY_CLASSES_ROOT = unchecked((IntPtr)(int)0x80000000);
        public static readonly IntPtr HKEY_CURRENT_USER = unchecked((IntPtr)(int)0x80000001);
        public static readonly IntPtr HKEY_LOCAL_MACHINE = unchecked((IntPtr)(int)0x80000002);
        public static readonly IntPtr HKEY_USERS = unchecked((IntPtr)(int)0x80000003);

        private const uint REG_SZ = 1;
        private const uint REG_DWORD = 4;
        private const uint REG_QWORD = 11;

        private const uint KEY_WRITE = 0x20006;
        private const uint KEY_WOW64_64KEY = 0x0100;
        private const uint STANDARD_RIGHTS_WRITE = 0x20000;
        private const uint DESIRED_ACCESS = KEY_WRITE | KEY_WOW64_64KEY;

        #endregion

        #region Native Methods

        [DllImport("ktmw32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeTransactionHandle CreateTransaction(
            IntPtr lpTransactionAttributes,
            IntPtr UOW,
            uint CreateOptions,
            uint IsolationLevel,
            uint IsolationFlags,
            uint Timeout,
            string Description);

        [DllImport("ktmw32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CommitTransaction(SafeTransactionHandle TransactionHandle);

        [DllImport("ktmw32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RollbackTransaction(SafeTransactionHandle TransactionHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegCreateKeyTransacted(
            IntPtr hKey,
            string lpSubKey,
            uint Reserved,
            string lpClass,
            uint dwOptions,
            uint samDesired,
            IntPtr lpSecurityAttributes,
            out SafeRegistryHandle phkResult,
            out uint lpdwDisposition,
            SafeTransactionHandle hTransaction,
            IntPtr pExtendedParameter);

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

        #endregion

        private SafeTransactionHandle _transactionHandle;
        private bool _isCommitted;
        private bool _isDisposed;
        private readonly string _description;

        public SafeTransactionHandle TransactionHandle => _transactionHandle;
        public bool IsActive => _transactionHandle != null && !_transactionHandle.IsInvalid && !_isCommitted && !_isDisposed;

        public event CoreMessageEventHandler OnOperationSuccess;
        public event CoreErrorEventHandler OnOperationError;

        public KtmTransactionManager(string description = "DWS_SystemOptimization")
        {
            _description = description;
            BeginTransaction();
        }

        public void BeginTransaction()
        {
            if (_transactionHandle != null && !_transactionHandle.IsInvalid)
            {
                throw new InvalidOperationException("A transaction is already active.");
            }

            _transactionHandle = CreateTransaction(IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, _description);
            if (_transactionHandle.IsInvalid)
            {
                var err = Marshal.GetLastWin32Error();
                var ex = new Win32Exception(err, $"Failed to create Kernel Transaction ({_description}). Error: {err}");
                TransactionJournal.Log(JournalEntryType.Error, "KTM CreateTransaction failed", ex);
                OnOperationError?.Invoke(this, ex);
                throw ex;
            }

            _isCommitted = false;
            TransactionJournal.Log(JournalEntryType.TransactionStart, $"Kernel Transaction started: {_description}");
        }

        public void SetDword(IntPtr hRootKey, string subKey, string valueName, int value)
        {
            EnsureActive();
            SafeRegistryHandle hSubKey = null;
            try
            {
                uint disposition;
                int result = RegCreateKeyTransacted(
                    hRootKey,
                    subKey,
                    0,
                    null,
                    0,
                    DESIRED_ACCESS,
                    IntPtr.Zero,
                    out hSubKey,
                    out disposition,
                    _transactionHandle,
                    IntPtr.Zero);

                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegCreateKeyTransacted failed on {subKey} (Code {result})");
                }

                byte[] data = BitConverter.GetBytes(value);
                result = RegSetValueEx(hSubKey, valueName, 0, REG_DWORD, data, (uint)data.Length);
                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegSetValueEx failed for {subKey}\\{valueName} (Code {result})");
                }

                string msg = $"Transacted Set DWORD: {subKey}\\{valueName} = {value}";
                TransactionJournal.Log(JournalEntryType.RegistryOperation, msg);
                OnOperationSuccess?.Invoke(this, msg);
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, $"Transacted SetDword failed on {subKey}\\{valueName}", ex);
                OnOperationError?.Invoke(this, ex);
                throw;
            }
            finally
            {
                hSubKey?.Dispose();
            }
        }

        public void SetString(IntPtr hRootKey, string subKey, string valueName, string value)
        {
            EnsureActive();
            SafeRegistryHandle hSubKey = null;
            try
            {
                uint disposition;
                int result = RegCreateKeyTransacted(
                    hRootKey,
                    subKey,
                    0,
                    null,
                    0,
                    DESIRED_ACCESS,
                    IntPtr.Zero,
                    out hSubKey,
                    out disposition,
                    _transactionHandle,
                    IntPtr.Zero);

                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegCreateKeyTransacted failed on {subKey} (Code {result})");
                }

                byte[] data = Encoding.Unicode.GetBytes((value ?? string.Empty) + "\0");
                result = RegSetValueEx(hSubKey, valueName, 0, REG_SZ, data, (uint)data.Length);
                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegSetValueEx failed for {subKey}\\{valueName} (Code {result})");
                }

                string msg = $"Transacted Set String: {subKey}\\{valueName} = '{value}'";
                TransactionJournal.Log(JournalEntryType.RegistryOperation, msg);
                OnOperationSuccess?.Invoke(this, msg);
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, $"Transacted SetString failed on {subKey}\\{valueName}", ex);
                OnOperationError?.Invoke(this, ex);
                throw;
            }
            finally
            {
                hSubKey?.Dispose();
            }
        }

        public void DeleteValue(IntPtr hRootKey, string subKey, string valueName)
        {
            EnsureActive();
            SafeRegistryHandle hSubKey = null;
            try
            {
                uint disposition;
                int result = RegCreateKeyTransacted(
                    hRootKey,
                    subKey,
                    0,
                    null,
                    0,
                    DESIRED_ACCESS,
                    IntPtr.Zero,
                    out hSubKey,
                    out disposition,
                    _transactionHandle,
                    IntPtr.Zero);

                if (result != 0)
                {
                    throw new Win32Exception(result, $"RegCreateKeyTransacted failed on {subKey} (Code {result})");
                }

                result = RegDeleteValue(hSubKey, valueName);
                // 2 = ERROR_FILE_NOT_FOUND (acceptable for deletes)
                if (result != 0 && result != 2)
                {
                    throw new Win32Exception(result, $"RegDeleteValue failed for {subKey}\\{valueName} (Code {result})");
                }

                string msg = $"Transacted Delete Value: {subKey}\\{valueName}";
                TransactionJournal.Log(JournalEntryType.RegistryOperation, msg);
                OnOperationSuccess?.Invoke(this, msg);
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, $"Transacted DeleteValue failed on {subKey}\\{valueName}", ex);
                OnOperationError?.Invoke(this, ex);
                throw;
            }
            finally
            {
                hSubKey?.Dispose();
            }
        }

        public void Commit()
        {
            EnsureActive();
            if (!CommitTransaction(_transactionHandle))
            {
                var err = Marshal.GetLastWin32Error();
                var ex = new Win32Exception(err, $"Kernel Transaction Commit failed with code: {err}");
                TransactionJournal.Log(JournalEntryType.Error, "KTM CommitTransaction failed, auto-rolling back", ex);
                OnOperationError?.Invoke(this, ex);
                Rollback();
                throw ex;
            }

            _isCommitted = true;
            TransactionJournal.Log(JournalEntryType.TransactionCommit, $"Kernel Transaction COMMITTED successfully: {_description}");
        }

        public void Rollback()
        {
            if (_transactionHandle == null || _transactionHandle.IsInvalid || _isCommitted)
            {
                return;
            }

            bool rolledBack = RollbackTransaction(_transactionHandle);
            if (!rolledBack)
            {
                var err = Marshal.GetLastWin32Error();
                TransactionJournal.Log(JournalEntryType.Warning, $"RollbackTransaction returned false (Error {err})");
            }
            else
            {
                TransactionJournal.Log(JournalEntryType.TransactionRollback, $"Kernel Transaction ROLLED BACK: {_description}");
            }
        }

        private void EnsureActive()
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("No active Kernel Transaction in progress.");
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            // If not committed, automatic atomic kernel rollback guarantees zero corruption
            if (!_isCommitted && _transactionHandle != null && !_transactionHandle.IsInvalid)
            {
                Rollback();
            }

            _transactionHandle?.Dispose();
            _transactionHandle = null;
            _isDisposed = true;
        }
    }
}
