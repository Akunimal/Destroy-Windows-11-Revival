using System.Text.Json.Serialization;
using DWS.Core.Logging;
using DWS.Core.Rules;

namespace DWS.Core.Json;

[JsonSerializable(typeof(RuleSet))]
[JsonSerializable(typeof(Rule))]
[JsonSerializable(typeof(List<Rule>))]
[JsonSerializable(typeof(RuleOperation))]
[JsonSerializable(typeof(List<RuleOperation>))]
[JsonSerializable(typeof(AuditReport))]
[JsonSerializable(typeof(AuditChangeItem))]
[JsonSerializable(typeof(List<AuditChangeItem>))]
[JsonSerializable(typeof(TransactionEntry))]
[JsonSerializable(typeof(List<TransactionEntry>))]
[JsonSerializable(typeof(TransactionHistoryItem))]
[JsonSerializable(typeof(List<TransactionHistoryItem>))]
[JsonSerializable(typeof(PipelineProgress))]
[JsonSerializable(typeof(PipelineResult))]
[JsonSerializable(typeof(SystemStatus))]
[JsonSerializable(typeof(ApplyRulesRequest))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class DwsJsonContext : JsonSerializerContext
{
}
