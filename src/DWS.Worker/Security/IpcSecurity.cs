using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace DWS.Worker.Security;

public static class IpcSecurity
{
    public const string PipeName = "DWS11_Enterprise_IPC_v2";
    public const string PipePath = @"\\.\pipe\" + PipeName;
    public const string TokenHeaderName = "X-DWS-IPC-Token";

    private static string? _currentToken;
    private static readonly object _lock = new();

    public static PipeSecurity CreatePipeSecurityDescriptor()
    {
        var pipeSecurity = new PipeSecurity();
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);

        // Explicit Deny remote access via SMB / IPC$
        pipeSecurity.AddAccessRule(new PipeAccessRule(networkSid, PipeAccessRights.FullControl, AccessControlType.Deny));
        pipeSecurity.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        return pipeSecurity;
    }

    public static string InitializeSessionToken()
    {
        lock (_lock)
        {
            if (_currentToken != null)
                return _currentToken;

            byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
            string token = Convert.ToHexString(tokenBytes);
            _currentToken = token;

            try
            {
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string dwsDir = Path.Combine(programData, "DWS");

                if (OperatingSystem.IsWindows())
                {
                    // Guard against pre-creation / junction / symlink attacks
                    if (Directory.Exists(dwsDir))
                    {
                        var attrs = File.GetAttributes(dwsDir);
                        if ((attrs & FileAttributes.ReparsePoint) != 0)
                        {
                            Directory.Delete(dwsDir, recursive: false);
                        }
                    }

                    Directory.CreateDirectory(dwsDir);

                    // Restrict directory permissions: Only SYSTEM and Built-in Administrators
                    var dirInfo = new DirectoryInfo(dwsDir);
                    var dirSecurity = new DirectorySecurity();
                    var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                    var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

                    dirSecurity.SetOwner(systemSid);
                    dirSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    dirSecurity.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    dirSecurity.AddAccessRule(new FileSystemAccessRule(adminSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    dirInfo.SetAccessControl(dirSecurity);

                    string tokenPath = Path.Combine(dwsDir, "ipc.token");

                    // Create file with explicit security before writing content
                    var fileInfo = new FileInfo(tokenPath);
                    var fileSecurity = new FileSecurity();
                    fileSecurity.SetOwner(systemSid);
                    fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    fileSecurity.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow));
                    fileSecurity.AddAccessRule(new FileSystemAccessRule(adminSid, FileSystemRights.FullControl, AccessControlType.Allow));

                    using (var fs = fileInfo.Create(FileMode.Create, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, fileSecurity))
                    {
                        byte[] rawBytes = Encoding.UTF8.GetBytes(token);
                        fs.Write(rawBytes, 0, rawBytes.Length);
                        fs.Flush(flushToDisk: true);
                    }
                }
                else
                {
                    Directory.CreateDirectory(dwsDir);
                    File.WriteAllText(Path.Combine(dwsDir, "ipc.token"), token, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not persist IPC token file: {ex.Message}");
            }

            return token;
        }
    }

    public static bool ValidateToken(string? candidateToken)
    {
        if (string.IsNullOrEmpty(_currentToken) || string.IsNullOrEmpty(candidateToken))
            return false;

        byte[] currentBytes = Encoding.UTF8.GetBytes(_currentToken);
        byte[] candidateBytes = Encoding.UTF8.GetBytes(candidateToken);

        // Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(currentBytes, candidateBytes);
    }

    /// <summary>
    /// Applies strict SDDL (Administrators + SYSTEM only, Deny Network) to the active Named Pipe.
    /// </summary>
    public static void ApplyPipeSecurity()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            // SDDL:
            // D: (DACL)
            // (D;;GA;;;NU)   - Deny Generic All to Network Users
            // (A;;GA;;;SY)   - Allow Generic All to SYSTEM
            // (A;;GA;;;BA)   - Allow Generic All to Builtin Administrators
            const string sddl = "D:(D;;GA;;;NU)(A;;GA;;;SY)(A;;GA;;;BA)";

            if (ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                1, // SDDL_REVISION_1
                out IntPtr pSd,
                out _))
            {
                try
                {
                    SetNamedSecurityInfo(
                        PipePath,
                        1, // SE_FILE_OBJECT
                        4, // DACL_SECURITY_INFORMATION
                        IntPtr.Zero,
                        IntPtr.Zero,
                        pSd,
                        IntPtr.Zero);
                }
                finally
                {
                    LocalFree(pSd);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] ApplyPipeSecurity: {ex.Message}");
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string StringSecurityDescriptor,
        uint StringSDRevision,
        out IntPtr SecurityDescriptor,
        out ulong SecurityDescriptorSize);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint SetNamedSecurityInfo(
        string pObjectName,
        int ObjectType,
        int SecurityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
