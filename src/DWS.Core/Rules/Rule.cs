using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DWS.Core.Rules;

public class Rule : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private bool _isApplied;

    public string Id { get; set; } = "";
    public string Category { get; set; } = ""; // "Privacy", "Performance", "Gaming", "UI", "Telemetry"
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public int MinBuild { get; set; } = 19045;
    public int? MaxBuild { get; set; }
    public bool RequiresTrustedInstaller { get; set; }
    public List<RuleOperation> Operations { get; set; } = new();

    // Estado en runtime
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (_isApplied != value)
            {
                _isApplied = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
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
    public string? PackagePattern { get; set; }
    public List<string>? Domains { get; set; }
}
