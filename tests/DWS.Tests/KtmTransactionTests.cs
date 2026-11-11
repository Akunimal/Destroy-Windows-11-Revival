using DWS.Core.KTM;
using Xunit;

namespace DWS.Tests;

public class KtmTransactionTests
{
    [Fact]
    public void KtmTransaction_InitializesWithValidHandle()
    {
        using var ktm = new KtmTransactionManager("DWS_Unit_Test_Tx");

        Assert.NotNull(ktm.TransactionHandle);
        Assert.False(ktm.TransactionHandle.IsInvalid);
        Assert.False(ktm.TransactionHandle.IsClosed);
        Assert.Equal("DWS_Unit_Test_Tx", ktm.Description);
    }

    [Fact]
    public void KtmTransaction_DoubleCommit_ThrowsInvalidOperationException()
    {
        using var ktm = new KtmTransactionManager("DWS_Unit_Test_Commit");
        ktm.Commit();

        Assert.Throws<InvalidOperationException>(() => ktm.Commit());
    }

    [Fact]
    public void KtmTransaction_OperationAfterDispose_ThrowsObjectDisposedException()
    {
        var ktm = new KtmTransactionManager("DWS_Unit_Test_Dispose");
        ktm.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ktm.Commit());
    }

    [Fact]
    public void KtmTransaction_SetDwordAndRollback_SucceedsWithoutLeaking()
    {
        if (!OperatingSystem.IsWindows()) return;

        string testSubKey = @"Software\DWS_Test_KtmAccess";
        string testVal = "TestVal";

        using (var ktm = new KtmTransactionManager("DWS_Unit_Test_SetRollback"))
        {
            ktm.SetDword(KtmTransactionManager.HKEY_CURRENT_USER, testSubKey, testVal, 42);
            ktm.Rollback();
        }

        // Key should not exist committed in standard registry
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testSubKey);
        Assert.Null(key);
    }

    [Fact]
    public void KtmTransaction_DeleteValueOnMissingKey_ReturnsCleanlyWithoutCreatingPhantomKey()
    {
        if (!OperatingSystem.IsWindows()) return;

        string missingKey = @"Software\DWS_MissingKey_Test_" + Guid.NewGuid().ToString("N");
        using (var ktm = new KtmTransactionManager("DWS_Unit_Test_DeleteMissing"))
        {
            // Should not throw or create the missing key
            ktm.DeleteValue("HKCU", missingKey, "NonExistentVal");
            ktm.Commit();
        }

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(missingKey);
        Assert.Null(key);
    }

    [Fact]
    public void KtmTransaction_CanUseTxR_EnforcesHiveAndImpersonationBoundaries()
    {
        // HKCU is supported
        Assert.True(KtmTransactionManager.CanUseTxR("HKCU", @"Software\Test", false));

        // HKLM\SOFTWARE is supported
        Assert.True(KtmTransactionManager.CanUseTxR("HKLM", @"SOFTWARE\Policies\Microsoft\SQMClient", false));
        Assert.True(KtmTransactionManager.CanUseTxR("HKLM", @"\SOFTWARE\Policies\Test", false));

        // HKLM\SYSTEM is NOT supported by TxR
        Assert.False(KtmTransactionManager.CanUseTxR("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", false));
        Assert.False(KtmTransactionManager.CanUseTxR("HKLM", @"SYSTEM\CurrentControlSet\Services\dmwappushservice", false));

        // Task cache schedule is excluded from TxR
        Assert.False(KtmTransactionManager.CanUseTxR("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache", false));

        // TrustedInstaller impersonation bypasses TxR completely
        Assert.False(KtmTransactionManager.CanUseTxR("HKLM", @"SOFTWARE\Policies\Microsoft", true));
        Assert.False(KtmTransactionManager.CanUseTxR("HKCU", @"Software\Test", true));
    }

    [Fact]
    public void DirectRegistryWrites_SetAndCleanlyDelete_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        string subKey = @"Software\DWS_DirectTest_" + Guid.NewGuid().ToString("N");
        string valName = "DirectVal";

        try
        {
            KtmTransactionManager.SetValueDirect("HKCU", subKey, valName, 12345);

            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey))
            {
                Assert.NotNull(key);
                Assert.Equal(12345, key.GetValue(valName));
            }

            KtmTransactionManager.DeleteValueDirect("HKCU", subKey, valName);

            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey))
            {
                Assert.NotNull(key);
                Assert.Null(key.GetValue(valName));
            }
        }
        finally
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(subKey); } catch { }
        }
    }
}
