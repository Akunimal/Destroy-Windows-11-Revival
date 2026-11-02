using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DWS.Core.KTM
{
    /// <summary>
    /// Safe handle wrapping a Kernel Transaction Manager (KTM) transaction handle.
    /// Ensures atomic release and prevents handle leaks.
    /// </summary>
    public sealed class SafeTransactionHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeTransactionHandle() : base(true)
        {
        }

        public SafeTransactionHandle(IntPtr preexistingHandle, bool ownsHandle) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }
}
