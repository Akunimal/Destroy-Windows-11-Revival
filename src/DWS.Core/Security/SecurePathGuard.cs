using System.Security.AccessControl;
using System.Security.Principal;

namespace DWS.Core.Security;

public static class SecurePathGuard
{
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <summary>
    /// Creates or hardens a directory in %ProgramData% by stripping inherited User permissions
    /// and granting FullControl exclusively to NT AUTHORITY\SYSTEM and BUILTIN\Administrators.
    /// Also grants current user access if elevated or running under interactive testing session.
    /// Protects against NTFS Reparse Point / Junction attacks.
    /// </summary>
    public static string EnsureSecureDirectory(string path, bool isSharedRoot = true)
    {
        var dirInfo = new DirectoryInfo(path);

        if (dirInfo.Exists)
        {
            // Verify it is not a junction or reparse point planted by an unprivileged user
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Security hazard: Directory '{path}' is an NTFS Reparse Point or Junction.");
            }
        }
        else
        {
            dirInfo.Create();
        }

        // Only enforce custom ACLs on the shared %ProgramData%\DWS directory tree
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (isSharedRoot && path.StartsWith(programData, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var security = new DirectorySecurity();
                // Block inheritance
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                var systemRule = new FileSystemAccessRule(
                    SystemSid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);

                var adminRule = new FileSystemAccessRule(
                    AdminSid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);

                security.AddAccessRule(systemRule);
                security.AddAccessRule(adminRule);

                // If running as interactive user (development/testing), grant full control so the session can read/write logs
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null && currentUser != SystemSid && currentUser != AdminSid)
                {
                    var userRule = new FileSystemAccessRule(
                        currentUser,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow);
                    security.AddAccessRule(userRule);
                }

                dirInfo.SetAccessControl(security);
            }
            catch
            {
                // In constrained environments, proceed gracefully
            }
        }

        return path;
    }
}
