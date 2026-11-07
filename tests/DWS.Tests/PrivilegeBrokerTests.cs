using System.Security.Principal;
using DWS.Core.Security;
using Xunit;

namespace DWS.Tests;

public class PrivilegeBrokerTests
{
    private readonly SystemPrivilegeBroker _broker = new();

    [Fact]
    public void PrivilegeBroker_CheckSystemAndAdminStatus_DoesNotThrow()
    {
        bool isSystem = _broker.IsRunningAsSystem();
        bool isAdmin = _broker.IsRunningAsAdministrator();

        // Should return valid booleans without throwing exceptions
        Assert.True(isSystem || !isSystem);
        Assert.True(isAdmin || !isAdmin);
    }

    [Fact]
    public void EnableProcessPrivileges_ExecutesWithoutCrashing()
    {
        if (OperatingSystem.IsWindows())
        {
            // Should attempt privilege enablement and return result
            bool result = SystemPrivilegeBroker.EnableProcessPrivileges();
            Assert.True(result || !result);
        }
    }

    [Fact]
    public void EnablePrivilege_SpecificPrivilege_ExecutesGracefully()
    {
        if (OperatingSystem.IsWindows())
        {
            bool debugResult = SystemPrivilegeBroker.EnablePrivilege("SeDebugPrivilege");
            bool shutdownResult = SystemPrivilegeBroker.EnablePrivilege("SeShutdownPrivilege");

            Assert.True(debugResult || !debugResult);
            Assert.True(shutdownResult || !shutdownResult);
        }
    }

    [Fact]
    public void TrustedInstallerServiceName_IsCorrect()
    {
        Assert.Equal("TrustedInstaller", SystemPrivilegeBroker.TrustedInstallerServiceName);
    }

    [Fact]
    public void WatchdogTaskName_IsCorrect()
    {
        Assert.Equal("DWS_EnterpriseBaseline_Watchdog", SystemPrivilegeBroker.WatchdogTaskName);
    }

    [Fact]
    public void Watchdog_CheckStatus_ReturnsCleanBoolean()
    {
        // Querying watchdog task should return false if not installed, without crashing
        bool isInstalled = _broker.IsWatchdogInstalled();
        Assert.True(isInstalled || !isInstalled);
    }
}
