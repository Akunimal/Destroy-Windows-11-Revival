namespace DWS.Core.Logging;

public class TransactionEntry
{
    public int Id { get; set; }
    public string TransactionId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Category { get; set; } = "";
    public ChangeType ChangeType { get; set; }
    public string RuleName { get; set; } = "";

    // Registro
    public string? Hive { get; set; }
    public string? KeyPath { get; set; }
    public string? ValueName { get; set; }
    public string? NewValue { get; set; }
    public string? PreviousValue { get; set; }
    public bool PreviousValueExisted { get; set; }

    // Servicios
    public string? ServiceName { get; set; }
    public string? PreviousServiceStartMode { get; set; }

    // Hosts y AppX
    public string? PreviousHostsBlock { get; set; }
    public string? PackagePattern { get; set; }
    public List<string>? BlockedDomains { get; set; }

    public TransactionStatus Status { get; set; } = TransactionStatus.Applied;

    // UI helpers
    public bool IsSelectedForRevert { get; set; }
    public string StatusColor => Status switch
    {
        TransactionStatus.Applied => "#00FF00",
        TransactionStatus.Reverted => "#FFA500",
        TransactionStatus.Failed => "#FF0000",
        _ => "#FFFFFF"
    };
}

public class TransactionHistoryItem
{
    public string TransactionId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public List<TransactionEntry> Changes { get; set; } = new();
    public List<string> Categories { get; set; } = new();
}

public enum ChangeType
{
    RegistrySet,
    RegistryDelete,
    ServiceConfig,
    LGPO,
    AppxRemoval,
    HostsModify
}

public enum TransactionStatus
{
    Applied,
    Reverted,
    Failed
}
