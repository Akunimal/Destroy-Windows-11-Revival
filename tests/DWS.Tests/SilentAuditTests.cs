using DWS.GUI;
using Xunit;

namespace DWS.Tests;

public class SilentAuditTests
{
    [Fact]
    public void RunSilentAudit_ExecutesSuccessfullyAndCreatesReport()
    {
        // Execute headless silent audit
        int exitCode = Program.RunSilentAudit();

        // 0 = all compliant, 2 = pending / non-compliant changes detected
        Assert.True(exitCode is 0 or 2, $"Expected exit code 0 or 2, got {exitCode}");

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string auditDir = Path.Combine(programData, "DWS", "audit");
        string latestReport = Path.Combine(auditDir, "latest_audit_report.json");

        Assert.True(File.Exists(latestReport), $"Expected audit report at '{latestReport}'");

        string content = File.ReadAllText(latestReport);
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Contains("totalRulesEvaluated", content);
    }
}
