using DWS.Core.Logging;
using Xunit;

namespace DWS.Tests;

public class TransactionJournalTests
{
    [Fact]
    public void Journal_LogsEntries_AndRetrievesHistoryInOrder()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"dws_journal_test_{Guid.NewGuid():N}");
        try
        {
            var journal = new TransactionJournal(tempDir);
            string txId = "TX_TEST_001";

            journal.LogChange(new TransactionEntry
            {
                TransactionId = txId,
                Timestamp = DateTime.UtcNow,
                Category = "Telemetry",
                ChangeType = ChangeType.RegistrySet,
                RuleName = "Telemetry Rule",
                Hive = "HKLM",
                KeyPath = @"SOFTWARE\Policies\Test",
                ValueName = "TestVal",
                NewValue = "0",
                PreviousValue = "1",
                PreviousValueExisted = true,
                Status = TransactionStatus.Applied
            });

            journal.LogChange(new TransactionEntry
            {
                TransactionId = txId,
                Timestamp = DateTime.UtcNow,
                Category = "AI",
                ChangeType = ChangeType.RegistrySet,
                RuleName = "Copilot Rule",
                Hive = "HKLM",
                KeyPath = @"SOFTWARE\Policies\TestCopilot",
                ValueName = "TurnOff",
                NewValue = "1",
                PreviousValue = null,
                PreviousValueExisted = false,
                Status = TransactionStatus.Applied
            });

            var history = journal.GetTransactionHistory();

            Assert.Single(history);
            Assert.Equal(txId, history[0].TransactionId);
            Assert.Equal(2, history[0].Changes.Count);
            Assert.Contains("Telemetry", history[0].Categories);
            Assert.Contains("AI", history[0].Categories);

            // Verify sequential ID assignment
            Assert.Equal(1, history[0].Changes[0].Id);
            Assert.Equal(2, history[0].Changes[1].Id);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void DefaultJournalDirectory_PointsToProgramData()
    {
        string expectedBase = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string defaultDir = TransactionJournal.DefaultJournalDirectory;

        Assert.StartsWith(expectedBase, defaultDir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("DWS", "journal"), defaultDir, StringComparison.OrdinalIgnoreCase);
    }
}
