using DWS.Core.Operations;
using DWS.Core.Rules;
using Xunit;

namespace DWS.Tests;

public class OperationHandlerTests
{
    [Theory]
    [InlineData("disabled", ServiceOperationHandler.SERVICE_DISABLED)]
    [InlineData("Disabled", ServiceOperationHandler.SERVICE_DISABLED)]
    [InlineData("4", ServiceOperationHandler.SERVICE_DISABLED)]
    [InlineData("manual", ServiceOperationHandler.SERVICE_DEMAND_START)]
    [InlineData("demand", ServiceOperationHandler.SERVICE_DEMAND_START)]
    [InlineData("3", ServiceOperationHandler.SERVICE_DEMAND_START)]
    [InlineData("auto", ServiceOperationHandler.SERVICE_AUTO_START)]
    [InlineData("automatic", ServiceOperationHandler.SERVICE_AUTO_START)]
    [InlineData("2", ServiceOperationHandler.SERVICE_AUTO_START)]
    [InlineData(null, ServiceOperationHandler.SERVICE_DISABLED)]
    [InlineData("", ServiceOperationHandler.SERVICE_DISABLED)]
    public void ServiceOperationHandler_ParseStartMode_ReturnsExpectedValues(string? mode, uint expected)
    {
        uint parsed = ServiceOperationHandler.ParseStartMode(mode);
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void ServiceOperationHandler_QueryNonExistentService_ReturnsGracefully()
    {
        var handler = new ServiceOperationHandler();
        var (success, startType, isRunning) = handler.QueryServiceState("DWS_NonExistent_Service_XYZ_9999");

        Assert.False(success);
        Assert.False(isRunning);
    }

    [Fact]
    public void AppxOperationHandler_SanitizePackageName_RemovesCommandInjectionChars()
    {
        string maliciousInput = "Microsoft.Copilot; Remove-Item C:\\ -Recurse -Force & calc.exe | echo 'pwned' $env:PATH";
        string sanitized = AppxOperationHandler.SanitizePackageName(maliciousInput);

        Assert.DoesNotContain(";", sanitized);
        Assert.DoesNotContain("&", sanitized);
        Assert.DoesNotContain("|", sanitized);
        Assert.DoesNotContain("'", sanitized);
        Assert.DoesNotContain("\"", sanitized);
        Assert.DoesNotContain("$", sanitized);
        Assert.DoesNotContain("\\", sanitized);
        Assert.DoesNotContain(" ", sanitized);
        Assert.Equal("Microsoft.CopilotRemove-ItemC-Recurse-Forcecalc.exeechopwnedenvPATH", sanitized);
    }

    [Fact]
    public async Task HostsOperationHandler_ManagedBlock_LifecycleAndIdempotence()
    {
        string tempHostsPath = Path.Combine(Path.GetTempPath(), $"hosts_test_{Guid.NewGuid():N}.txt");

        try
        {
            string initialContent = "# Test Hosts File\r\n127.0.0.1 localhost\r\n";
            File.WriteAllText(tempHostsPath, initialContent, System.Text.Encoding.UTF8);

            // 1. Initial state: No block
            var (hasBlockInitial, _) = HostsOperationHandler.ReadManagedBlock(tempHostsPath);
            Assert.False(hasBlockInitial);

            // 2. Apply block with domains
            var domains = new List<string> { "telemetry.test.microsoft.com", "v10.events.test.com" };
            await HostsOperationHandler.ApplyHostsBlockAsync(domains, tempHostsPath);

            var (hasBlockAfterApply, blockContent) = HostsOperationHandler.ReadManagedBlock(tempHostsPath);
            Assert.True(hasBlockAfterApply);
            Assert.NotNull(blockContent);
            Assert.Contains(HostsOperationHandler.BeginMarker, blockContent);
            Assert.Contains(HostsOperationHandler.EndMarker, blockContent);
            Assert.Contains("0.0.0.0 telemetry.test.microsoft.com", blockContent);
            Assert.Contains("0.0.0.0 v10.events.test.com", blockContent);

            // 3. Idempotent re-apply with new domain
            var updatedDomains = new List<string> { "telemetry.test.microsoft.com", "new.domain.test" };
            await HostsOperationHandler.ApplyHostsBlockAsync(updatedDomains, tempHostsPath);

            string fileContent = File.ReadAllText(tempHostsPath);
            // Must contain exactly ONE BeginMarker and ONE EndMarker
            int countBegin = (fileContent.Length - fileContent.Replace(HostsOperationHandler.BeginMarker, "").Length) / HostsOperationHandler.BeginMarker.Length;
            int countEnd = (fileContent.Length - fileContent.Replace(HostsOperationHandler.EndMarker, "").Length) / HostsOperationHandler.EndMarker.Length;

            Assert.Equal(1, countBegin);
            Assert.Equal(1, countEnd);
            Assert.Contains("0.0.0.0 new.domain.test", fileContent);

            // 4. Remove block
            bool removed = await HostsOperationHandler.RemoveHostsBlockAsync(tempHostsPath);
            Assert.True(removed);

            var (hasBlockFinal, _) = HostsOperationHandler.ReadManagedBlock(tempHostsPath);
            Assert.False(hasBlockFinal);
            string finalContent = File.ReadAllText(tempHostsPath);
            Assert.DoesNotContain(HostsOperationHandler.BeginMarker, finalContent);
            Assert.Contains("127.0.0.1 localhost", finalContent);
        }
        finally
        {
            try { if (File.Exists(tempHostsPath)) File.Delete(tempHostsPath); } catch { }
        }
    }

    [Fact]
    public async Task Coordinator_DryRun_WithMultiOperationRules_EvaluatesAllTypes()
    {
        var coordinator = new DWS.Core.EnterprisePipelineCoordinator();

        // Target rules or evaluate dry run across all loaded rules
        var result = await coordinator.ExecuteEnterpriseBaselineAsync(
            targetRuleIds: null,
            isDryRun: true);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.IsDryRun);
        Assert.NotNull(result.AuditReport);
        Assert.True(result.AuditReport.TotalRulesEvaluated >= 35);
    }
}
