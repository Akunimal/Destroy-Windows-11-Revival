namespace DWS.Core.Rules;

public class Rule
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = ""; // "Privacy", "Performance", "Gaming", "UI", "Telemetry"
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public int MinBuild { get; set; }
    public int? MaxBuild { get; set; }
    public bool RequiresTrustedInstaller { get; set; }
    public List<RuleOperation> Operations { get; set; } = new();

    // Estado en runtime
    public bool IsSelected { get; set; }
    public bool IsApplied { get; set; }
}

public class RuleOperation
{
    public string Type { get; set; } = ""; // "registry", "service", "appx", "lgpo"
    public string? Hive { get; set; } // "HKLM", "HKCU"
    public string? Path { get; set; }
    public string? Name { get; set; }
    public string? Kind { get; set; } // "DWord", "String", "QWord"
    public string? Value { get; set; }
    public string? ServiceName { get; set; }
    public string? ServiceStartMode { get; set; }
}
