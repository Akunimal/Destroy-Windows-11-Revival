using DWS.Core;
using Xunit;

namespace DWS.Tests;

public class CoordinatorDryRunTests
{
    [Fact]
    public async Task Coordinator_DryRun_EvaluatesAllRulesWithoutSystemModifications()
    {
        var coordinator = new EnterprisePipelineCoordinator();
        var reportedProgress = new List<PipelineProgress>();
        var progress = new Progress<PipelineProgress>(p => reportedProgress.Add(p));

        var result = await coordinator.ExecuteEnterpriseBaselineAsync(
            targetRuleIds: null,
            isDryRun: true,
            progress: progress);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.False(result.AbortedByUser);
        Assert.True(result.IsDryRun);
        Assert.True(result.TotalRules >= 35, $"Expected at least 35 rules, but got {result.TotalRules}");
        Assert.Equal(result.TotalRules, result.AppliedRules);

        Assert.NotNull(result.AuditReport);
        Assert.Equal(result.TotalRules, result.AuditReport.TotalRulesEvaluated);
        Assert.True(result.AuditReport.PendingChangesCount + result.AuditReport.CompliantRulesCount > 0);

        // Verify progress updates occurred
        Assert.NotEmpty(reportedProgress);
        Assert.Contains(reportedProgress, p => p.PercentComplete >= 99);
    }

    [Fact]
    public async Task Coordinator_TargetRuleSubset_DryRunOnlyEvaluatesTarget()
    {
        var coordinator = new EnterprisePipelineCoordinator();
        var targetRuleIds = new List<string> { "create_restore_point", "disable_telemetry" };

        var result = await coordinator.ExecuteEnterpriseBaselineAsync(
            targetRuleIds: targetRuleIds,
            isDryRun: true);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.IsDryRun);
        Assert.Equal(2, result.AppliedRules);
        Assert.NotNull(result.AuditReport);
        Assert.Equal(2, result.AuditReport.TotalRulesEvaluated);
    }

    [Fact]
    public async Task Coordinator_Cancellation_AbortsCleanly()
    {
        var coordinator = new EnterprisePipelineCoordinator();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var result = await coordinator.ExecuteEnterpriseBaselineAsync(
            targetRuleIds: null,
            isDryRun: true,
            cancellationToken: cts.Token);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.True(result.AbortedByUser);
    }
}
