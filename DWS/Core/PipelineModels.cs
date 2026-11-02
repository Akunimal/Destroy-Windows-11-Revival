using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DWS.Core.Logging;

namespace DWS.Core
{
    public interface IProgress<T>
    {
        void Report(T value);
    }

    public class Progress<T> : IProgress<T>
    {
        private readonly SynchronizationContext _context;
        private readonly Action<T> _handler;

        public Progress(Action<T> handler)
        {
            _context = SynchronizationContext.Current ?? new SynchronizationContext();
            _handler = handler;
        }

        public void Report(T value)
        {
            if (_handler == null) return;
            if (SynchronizationContext.Current == _context)
            {
                _handler(value);
            }
            else
            {
                _context.Post(state => _handler((T)state), value);
            }
        }
    }

    public sealed class PipelineProgress
    {
        public string CurrentRuleName { get; set; }
        public int PercentComplete { get; set; }
        public JournalEntryType LogLevel { get; set; }
        public string Message { get; set; }

        public PipelineProgress()
        {
            CurrentRuleName = string.Empty;
            PercentComplete = 0;
            LogLevel = JournalEntryType.Info;
            Message = string.Empty;
        }

        public PipelineProgress(int percent, string message, string ruleName = null, JournalEntryType level = JournalEntryType.Info)
        {
            PercentComplete = percent;
            Message = message ?? string.Empty;
            CurrentRuleName = ruleName ?? string.Empty;
            LogLevel = level;
        }
    }

    public sealed class AuditChangeItem
    {
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string Hive { get; set; }
        public string KeyPath { get; set; }
        public string ValueName { get; set; }
        public string CurrentValue { get; set; }
        public string DesiredValue { get; set; }
        public string ActionType { get; set; } // MATCH, CREATE, CHANGE, ERROR
        public string Risk { get; set; }       // Low, Medium, High
    }

    public sealed class AuditReport
    {
        public DateTime TimestampUtc { get; set; }
        public string TargetOs { get; set; }
        public int TotalRulesEvaluated { get; set; }
        public int PendingChangesCount { get; set; }
        public int CompliantRulesCount { get; set; }
        public List<AuditChangeItem> PendingChanges { get; set; }
        public List<string> ServicesToStop { get; set; }
        public string OverallRiskLevel { get; set; }

        public AuditReport()
        {
            TimestampUtc = DateTime.UtcNow;
            TargetOs = Environment.OSVersion.VersionString;
            PendingChanges = new List<AuditChangeItem>();
            ServicesToStop = new List<string>();
            OverallRiskLevel = "Low";
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"timestampUtc\": \"{TimestampUtc:O}\",");
            sb.AppendLine($"  \"targetOs\": \"{EscapeJson(TargetOs)}\",");
            sb.AppendLine($"  \"totalRulesEvaluated\": {TotalRulesEvaluated},");
            sb.AppendLine($"  \"pendingChangesCount\": {PendingChangesCount},");
            sb.AppendLine($"  \"compliantRulesCount\": {CompliantRulesCount},");
            sb.AppendLine($"  \"overallRiskLevel\": \"{OverallRiskLevel}\",");

            sb.AppendLine("  \"servicesToStop\": [");
            for (int i = 0; i < ServicesToStop.Count; i++)
            {
                sb.Append($"    \"{EscapeJson(ServicesToStop[i])}\"");
                if (i < ServicesToStop.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"pendingChanges\": [");
            for (int i = 0; i < PendingChanges.Count; i++)
            {
                var item = PendingChanges[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"ruleId\": \"{EscapeJson(item.RuleId)}\",");
                sb.AppendLine($"      \"ruleName\": \"{EscapeJson(item.RuleName)}\",");
                sb.AppendLine($"      \"hive\": \"{EscapeJson(item.Hive)}\",");
                sb.AppendLine($"      \"keyPath\": \"{EscapeJson(item.KeyPath)}\",");
                sb.AppendLine($"      \"valueName\": \"{EscapeJson(item.ValueName)}\",");
                sb.AppendLine($"      \"currentValue\": \"{EscapeJson(item.CurrentValue)}\",");
                sb.AppendLine($"      \"desiredValue\": \"{EscapeJson(item.DesiredValue)}\",");
                sb.AppendLine($"      \"actionType\": \"{EscapeJson(item.ActionType)}\",");
                sb.AppendLine($"      \"risk\": \"{EscapeJson(item.Risk)}\"");
                sb.Append("    }");
                if (i < PendingChanges.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ]");
            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }
    }

    public sealed class PipelineResult
    {
        public bool Success { get; set; }
        public bool AbortedByUser { get; set; }
        public bool IsDryRun { get; set; }
        public string ErrorMessage { get; set; }
        public AuditReport AuditReport { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
