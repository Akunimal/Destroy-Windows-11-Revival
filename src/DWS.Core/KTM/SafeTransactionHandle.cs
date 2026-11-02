using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DWS.Core.KTM;

public sealed partial class SafeTransactionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeTransactionHandle() : base(true) { }

    public SafeTransactionHandle(IntPtr preexistingHandle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(preexistingHandle);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    protected override bool ReleaseHandle()
    {
        return CloseHandle(handle);
    }
}
