using System.Text.Json;
using DWS.Core;
using DWS.Core.Json;
using DWS.Core.Logging;
using DWS.Core.Rules;
using Xunit;

namespace DWS.Tests;

public class JsonSerializationTests
{
    [Fact]
    public void RuleSet_SerializesAndDeserializes_WithSourceGenerator()
    {
        var original = new RuleSet
        {
            SchemaVersion = "2.0.0",
            Signature = "DEFAULT_BUILTIN_ENTERPRISE_PINNED_V2",
            Rules = new List<Rule>
            {
                new()
                {
                    Id = "test_rule",
                    Category = "Privacy",
                    DisplayName = "Test Display Name",
                    Description = "Test description",
                    MinBuild = 19045,
                    IsSelected = true,
                    Operations = new List<RuleOperation>
                    {
                        new()
                        {
                            Type = "registry",
                            Hive = "HKLM",
                            Path = @"SOFTWARE\Policies\Test",
                            Name = "TestValue",
                            Kind = "DWord",
                            Value = "1"
                        }
                    }
                }
            }
        };

        string json = JsonSerializer.Serialize(original, DwsJsonContext.Default.RuleSet);
        Assert.Contains("\"schemaVersion\": \"2.0.0\"", json);
        Assert.Contains("\"id\": \"test_rule\"", json);

        var deserialized = JsonSerializer.Deserialize(json, DwsJsonContext.Default.RuleSet);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Rules);
        Assert.Equal("test_rule", deserialized.Rules[0].Id);
        Assert.Equal("1", deserialized.Rules[0].Operations[0].Value);
    }

    [Fact]
    public void PipelineResult_SerializesAndDeserializes_WithSourceGenerator()
    {
        var result = new PipelineResult
        {
            Success = true,
            AbortedByUser = false,
            IsDryRun = true,
            TransactionId = "TX_20261102_TEST",
            TotalRules = 35,
            AppliedRules = 35,
            Duration = TimeSpan.FromSeconds(2.5),
            AuditReport = new AuditReport
            {
                TotalRulesEvaluated = 35,
                PendingChangesCount = 12,
                CompliantRulesCount = 23,
                OverallRiskLevel = "Low"
            }
        };

        string json = JsonSerializer.Serialize(result, DwsJsonContext.Default.PipelineResult);
        var deserialized = JsonSerializer.Deserialize(json, DwsJsonContext.Default.PipelineResult);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.True(deserialized.IsDryRun);
        Assert.Equal("TX_20261102_TEST", deserialized.TransactionId);
        Assert.NotNull(deserialized.AuditReport);
        Assert.Equal(12, deserialized.AuditReport.PendingChangesCount);
        Assert.Equal(23, deserialized.AuditReport.CompliantRulesCount);
    }

    [Fact]
    public void TransactionEntry_SerializesAndDeserializes_WithSourceGenerator()
    {
        var entries = new List<TransactionEntry>
        {
            new()
            {
                Id = 1,
                TransactionId = "TX_001",
                Timestamp = DateTime.UtcNow,
                Category = "Telemetry",
                ChangeType = ChangeType.RegistrySet,
                RuleName = "Disable Telemetry",
                Hive = "HKLM",
                KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                ValueName = "AllowTelemetry",
                NewValue = "0",
                PreviousValue = "1",
                PreviousValueExisted = true,
                Status = TransactionStatus.Applied
            }
        };

        string json = JsonSerializer.Serialize(entries, DwsJsonContext.Default.ListTransactionEntry);
        var deserialized = JsonSerializer.Deserialize(json, DwsJsonContext.Default.ListTransactionEntry);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized);
        Assert.Equal("TX_001", deserialized[0].TransactionId);
        Assert.Equal(ChangeType.RegistrySet, deserialized[0].ChangeType);
        Assert.Equal(TransactionStatus.Applied, deserialized[0].Status);
    }

    [Fact]
    public void SystemStatus_SerializesAndDeserializes_WithSourceGenerator()
    {
        var status = new SystemStatus(
            Timestamp: DateTime.UtcNow,
            TotalRules: 35,
            AppliedRules: 20,
            PendingRules: 15,
            WatchdogInstalled: true,
            BitLockerStatus: "Protected (On)"
        );

        string json = JsonSerializer.Serialize(status, DwsJsonContext.Default.SystemStatus);
        var deserialized = JsonSerializer.Deserialize(json, DwsJsonContext.Default.SystemStatus);

        Assert.NotNull(deserialized);
        Assert.Equal(35, deserialized.TotalRules);
        Assert.Equal(20, deserialized.AppliedRules);
        Assert.True(deserialized.WatchdogInstalled);
        Assert.Equal("Protected (On)", deserialized.BitLockerStatus);
    }
}
