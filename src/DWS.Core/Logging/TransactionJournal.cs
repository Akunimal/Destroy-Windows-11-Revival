using System.ComponentModel;
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

    public static string DefaultJournalDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DWS", "journal");

    public TransactionJournal(string? basePath = null, ILogger<TransactionJournal>? logger = null)
    {
        _journalDirectory = basePath != null
            ? Path.Combine(basePath, "journal")
            : DefaultJournalDirectory;

        try
        {
            Security.SecurePathGuard.EnsureSecureDirectory(_journalDirectory);
        }
        catch (Exception ex)
        {
            // Fallback gracefully to local directory if %ProgramData% is somehow inaccessible
            _journalDirectory = Path.Combine(AppContext.BaseDirectory, "journal");
            Directory.CreateDirectory(_journalDirectory);
            logger?.LogWarning(ex, "Failed to create journal in ProgramData, falling back to local: {Dir}", _journalDirectory);
        }

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

        try
        {
            // Undo changes in reverse chronological order (LIFO) for transactional integrity
            foreach (var entry in appliedEntries.AsEnumerable().Reverse())
            {
                await RevertSingleEntryAsync(entry);
                entry.Status = TransactionStatus.Reverted;
            }

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

        try
        {
            // Reverse chronological order (LIFO)
            foreach (var entry in categoryEntries.AsEnumerable().Reverse())
            {
                await RevertSingleEntryAsync(entry);
                entry.Status = TransactionStatus.Reverted;
            }

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

        try
        {
            // Reverse chronological order (LIFO)
            foreach (var entry in targetEntries.AsEnumerable().Reverse())
            {
                await RevertSingleEntryAsync(entry);
                entry.Status = TransactionStatus.Reverted;
            }

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

    private static async Task RevertSingleEntryAsync(TransactionEntry entry)
    {
        switch (entry.ChangeType)
        {
            case ChangeType.RegistrySet:
                try
                {
                    if (entry.PreviousValueExisted && entry.PreviousValue != null)
                    {
                        KtmTransactionManager.SetValueDirect(entry.Hive!, entry.KeyPath!, entry.ValueName!, entry.PreviousValue);
                    }
                    else
                    {
                        KtmTransactionManager.DeleteValueDirect(entry.Hive!, entry.KeyPath!, entry.ValueName!);
                        if (!entry.PreviousKeyExisted && !string.IsNullOrEmpty(entry.KeyPath))
                        {
                            KtmTransactionManager.DeleteKeyIfEmptyDirect(entry.Hive!, entry.KeyPath!);
                        }
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
                {
                    // Reactive TrustedInstaller fallback on Access Denied during revert
                    try
                    {
                        Security.SystemPrivilegeBroker.RunAsTrustedInstaller(() =>
                        {
                            if (entry.PreviousValueExisted && entry.PreviousValue != null)
                            {
                                KtmTransactionManager.SetValueDirect(entry.Hive!, entry.KeyPath!, entry.ValueName!, entry.PreviousValue);
                            }
                            else
                            {
                                KtmTransactionManager.DeleteValueDirect(entry.Hive!, entry.KeyPath!, entry.ValueName!);
                                if (!entry.PreviousKeyExisted && !string.IsNullOrEmpty(entry.KeyPath))
                                {
                                    KtmTransactionManager.DeleteKeyIfEmptyDirect(entry.Hive!, entry.KeyPath!);
                                }
                            }
                        });
                    }
                    catch
                    {
                        // Protected key warning, isolate to allow remaining reverts to commit
                    }
                }
                catch
                {
                    // Non-fatal isolation to allow other entries to revert
                }
                break;

            case ChangeType.ServiceConfig:
                try
                {
                    if (!string.IsNullOrEmpty(entry.ServiceName) && !string.IsNullOrEmpty(entry.PreviousServiceStartMode))
                    {
                        var serviceHandler = new Operations.ServiceOperationHandler();
                        uint startType = Operations.ServiceOperationHandler.ParseStartMode(entry.PreviousServiceStartMode);
                        serviceHandler.ConfigureServiceStartType(entry.ServiceName, startType, null);
                    }
                }
                catch
                {
                    // Service rollback fault isolation
                }
                break;

            case ChangeType.HostsModify:
                try
                {
                    if (string.IsNullOrEmpty(entry.PreviousHostsBlock))
                    {
                        await Operations.HostsOperationHandler.RemoveHostsBlockAsync();
                    }
                }
                catch
                {
                    // Hosts rollback fault isolation
                }
                break;

            case ChangeType.AppxRemoval:
                try
                {
                    if (!string.IsNullOrEmpty(entry.PackagePattern))
                    {
                        Operations.AppxOperationHandler.RevertDeprovisioning(entry.PackagePattern);
                    }
                }
                catch
                {
                    // AppX rollback fault isolation
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
