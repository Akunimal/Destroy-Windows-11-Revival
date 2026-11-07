using DWS.Core.Logging;

namespace DWS.Core;

public class PipelineProgress
{
    public string CurrentRuleName { get; init; } = "";
    public double PercentComplete { get; init; }
    public string LogLevel { get; init; } = "Information";
    public string Message { get; init; } = "";

    public PipelineProgress() { }

    public PipelineProgress(double percent, string message, string ruleName = "", string logLevel = "Information")
    {
        PercentComplete = percent;
        Message = message;
        CurrentRuleName = ruleName;
        LogLevel = logLevel;
    }
}

public class AuditChangeItem
{
    public string RuleId { get; set; } = "";
    public string RuleName { get; set; } = "";
    public string Hive { get; set; } = "";
    public string KeyPath { get; set; } = "";
    public string ValueName { get; set; } = "";
    public string CurrentValue { get; set; } = "";
    public string DesiredValue { get; set; } = "";
    public string ActionType { get; set; } = ""; // MATCH, CREATE, CHANGE, ERROR
    public string Risk { get; set; } = "Low";
}

public class AuditReport
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string TargetOs { get; set; } = Environment.OSVersion.VersionString;
    public int TotalRulesEvaluated { get; set; }
    public int PendingChangesCount { get; set; }
    public int CompliantRulesCount { get; set; }
    public int TotalChanges => PendingChangesCount;
    public string OverallRiskLevel { get; set; } = "Low";
    public string RiskLevel => OverallRiskLevel;
    public List<AuditChangeItem> PendingChanges { get; set; } = new();
    public List<string> ServicesToStop { get; set; } = new();
}

public class PipelineResult
{
    public bool Success { get; set; }
    public bool AbortedByUser { get; set; }
    public bool IsDryRun { get; set; }
    public string? ErrorMessage { get; set; }
    public AuditReport? AuditReport { get; set; }
    public TimeSpan Duration { get; set; }
    public int TotalRules { get; set; }
    public int AppliedRules { get; set; }
    public string TransactionId { get; set; } = "";
}

public record SystemStatus(
    DateTime Timestamp,
    int TotalRules,
    int AppliedRules,
    int PendingRules,
    bool WatchdogInstalled,
    string BitLockerStatus
);

public record ApplyRulesRequest(
    List<string>? RuleIds = null,
    bool DryRun = false,
    string? Profile = null
);

public record HealthResponse(
    string Status,
    string Os,
    DateTime Timestamp
);
