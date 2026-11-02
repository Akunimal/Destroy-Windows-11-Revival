using System.Diagnostics;
using System.Text.Json;
using DWS.Core.Json;
using DWS.Core.KTM;
using Microsoft.Extensions.Logging;

namespace DWS.Core.Logging;

public class TransactionJournal
{
    private readonly string _journalDirectory;
    private readonly ILogger<TransactionJournal>? _logger;

    public TransactionJournal(string? basePath = null, ILogger<TransactionJournal>? logger = null)
    {
        _journalDirectory = Path.Combine(basePath ?? AppContext.BaseDirectory, "journal");
        Directory.CreateDirectory(_journalDirectory);
        _logger = logger;
    }

    public void LogChange(TransactionEntry entry)
    {
        var journalFile = Path.Combine(_journalDirectory, $"{entry.TransactionId}.json");
        var entries = LoadEntries(entry.TransactionId);
        entry.Id = entries.Count + 1;
        entries.Add(entry);

        var json = JsonSerializer.Serialize(entries, DwsJsonContext.Default.ListTransactionEntry);

        // Atomic file write
        var tempFile = journalFile + ".tmp";
        File.WriteAllText(tempFile, json);
        File.Move(tempFile, journalFile, overwrite: true);

        _logger?.LogDebug("Logged change for transaction {TransactionId}", entry.TransactionId);
    }

    public List<TransactionHistoryItem> GetTransactionHistory()
    {
        var history = new List<TransactionHistoryItem>();

        if (!Directory.Exists(_journalDirectory))
            return history;

        foreach (var file in Directory.GetFiles(_journalDirectory, "*.json"))
        {
            try
            {
                var entries = LoadEntries(Path.GetFileNameWithoutExtension(file));
                if (entries.Count > 0)
                {
                    history.Add(new TransactionHistoryItem
                    {
                        TransactionId = entries[0].TransactionId,
                        Timestamp = entries[0].Timestamp,
                        Changes = entries,
                        Categories = entries.Select(e => e.Category).Distinct().ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load transaction from {File}", file);
            }
        }

        return history.OrderByDescending(t => t.Timestamp).ToList();
    }

    public async Task<bool> RevertTransactionAsync(string transactionId)
    {
        var entries = LoadEntries(transactionId);
        var appliedEntries = entries.Where(e => e.Status == TransactionStatus.Applied).ToList();

        if (appliedEntries.Count == 0)
        {
            return false;
        }

        _logger?.LogInformation("Reverting full transaction {TransactionId} ({Count} entries)",
            transactionId, appliedEntries.Count);

        using var ktm = new KtmTransactionManager($"Revert_{transactionId}_Full");

        try
        {
            foreach (var entry in appliedEntries)
            {
                await RevertSingleEntryAsync(entry, ktm);
                entry.Status = TransactionStatus.Reverted;
            }

            ktm.Commit();
            SaveEntries(transactionId, entries);

            _logger?.LogInformation("Successfully reverted transaction {TransactionId}", transactionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to revert transaction {TransactionId}", transactionId);
            return false;
        }
    }

    public async Task<bool> RevertByCategoryAsync(string transactionId, string category)
    {
        var entries = LoadEntries(transactionId);
        var categoryEntries = entries.Where(e => e.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && e.Status == TransactionStatus.Applied).ToList();

        if (categoryEntries.Count == 0)
        {
            _logger?.LogInformation("No entries found for category {Category} in transaction {TransactionId}",
                category, transactionId);
            return false;
        }

        _logger?.LogInformation("Reverting {Count} entries for category {Category}",
            categoryEntries.Count, category);

        using var ktm = new KtmTransactionManager($"Revert_{transactionId}_{category}");

        try
        {
            foreach (var entry in categoryEntries)
            {
                await RevertSingleEntryAsync(entry, ktm);
                entry.Status = TransactionStatus.Reverted;
            }

            ktm.Commit();
            SaveEntries(transactionId, entries);

            _logger?.LogInformation("Successfully reverted category {Category}", category);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to revert category {Category}", category);
            return false;
        }
    }

    public async Task<bool> RevertSpecificEntriesAsync(string transactionId, List<int> entryIds)
    {
        var entries = LoadEntries(transactionId);
        var targetEntries = entries.Where(e => entryIds.Contains(e.Id) && e.Status == TransactionStatus.Applied).ToList();

        if (targetEntries.Count == 0)
        {
            _logger?.LogInformation("No matching entries found for revert");
            return false;
        }

        _logger?.LogInformation("Reverting {Count} specific entries", targetEntries.Count);

        using var ktm = new KtmTransactionManager($"Revert_{transactionId}_specific");

        try
        {
            foreach (var entry in targetEntries)
            {
                await RevertSingleEntryAsync(entry, ktm);
                entry.Status = TransactionStatus.Reverted;
            }

            ktm.Commit();
            SaveEntries(transactionId, entries);

            _logger?.LogInformation("Successfully reverted {Count} entries", targetEntries.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to revert specific entries");
            return false;
        }
    }

    private static async Task RevertSingleEntryAsync(TransactionEntry entry, KtmTransactionManager ktm)
    {
        switch (entry.ChangeType)
        {
            case ChangeType.RegistrySet:
                if (entry.PreviousValueExisted && entry.PreviousValue != null)
                {
                    ktm.SetValue(entry.Hive!, entry.KeyPath!, entry.ValueName!, entry.PreviousValue);
                }
                else
                {
                    ktm.DeleteValue(entry.Hive!, entry.KeyPath!, entry.ValueName!);
                }
                break;

            case ChangeType.ServiceConfig:
                if (!string.IsNullOrEmpty(entry.ServiceName) && !string.IsNullOrEmpty(entry.PreviousServiceStartMode))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"config \"{entry.ServiceName}\" start= {entry.PreviousServiceStartMode}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync();
                    }
                }
                break;
        }
    }

    private List<TransactionEntry> LoadEntries(string transactionId)
    {
        var file = Path.Combine(_journalDirectory, $"{transactionId}.json");
        if (!File.Exists(file))
            return new List<TransactionEntry>();

        var json = File.ReadAllText(file);
        return JsonSerializer.Deserialize(json, DwsJsonContext.Default.ListTransactionEntry)
               ?? new List<TransactionEntry>();
    }

    private void SaveEntries(string transactionId, List<TransactionEntry> entries)
    {
        var file = Path.Combine(_journalDirectory, $"{transactionId}.json");
        var json = JsonSerializer.Serialize(entries, DwsJsonContext.Default.ListTransactionEntry);

        var tempFile = file + ".tmp";
        File.WriteAllText(tempFile, json);
        File.Move(tempFile, file, overwrite: true);
    }
}
